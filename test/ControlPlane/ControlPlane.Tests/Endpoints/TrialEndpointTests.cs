using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class TrialEndpointTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;

    public TrialEndpointTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    // ── Trial starts on tenant creation (AC1) ────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_ResponseIncludesTrialInfo()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Trial Corp", Email = "trial@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();
        body!.Trial.Should().NotBeNull();
        body.Trial!.IsOnTrial.Should().BeTrue();
        body.Trial.HasUsedTrial.Should().BeTrue();
        body.Trial.TrialPlan.Should().Be("pro");
        body.Trial.TrialEndsAt.Should().NotBeNull();
        body.Trial.DaysRemaining.Should().Be(14);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_StartsWithProFeatures()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Pro Corp", Email = "prox@example.com" });

        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();
        body!.Plan.Should().Be("pro");
        body.Status.Should().Be("trialing");
        body.Limits.MaxDomains.Should().Be(50);
        body.Limits.MaxLinksPerDomain.Should().Be(10_000);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_TrialEndsAtIsApproximately14DaysFromNow()
    {
        var client = _factory.CreateClient();
        var before = DateTime.UtcNow.AddDays(14).AddSeconds(-5);

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Time Corp", Email = "time@example.com" });

        var after = DateTime.UtcNow.AddDays(14).AddSeconds(5);
        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();
        body!.Trial!.TrialEndsAt.Should().BeAfter(before).And.BeBefore(after);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_TrialFieldsPersistedInRepository()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Persist Corp", Email = "persist-trial@example.com" });

        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();
        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var tenant = await repo.GetByIdAsync(body!.Id);

        tenant!.TrialEndsAt.Should().NotBeNull();
        tenant.TrialPlan.Should().Be("pro");
        tenant.Status.Should().Be("trialing");
        tenant.Plan.Should().Be("pro");
    }

    // ── GET /tenants/{id}/trial — trial status visible in dashboard (AC2) ─

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetTrialStatus_ReturnsCurrentTrialInfo()
    {
        var tenantId = Guid.NewGuid();
        await SeedTrialingTenantAsync(tenantId);

        var (client, _) = SetupAdmin(tenantId);
        var response = await client.GetAsync($"/api/v1/tenants/{tenantId}/trial");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TrialStatusResponse>();
        body!.IsOnTrial.Should().BeTrue();
        body.HasUsedTrial.Should().BeTrue();
        body.TrialPlan.Should().Be("pro");
        body.DaysRemaining.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetTrialStatus_WhenTrialExpired_IsOnTrialFalse()
    {
        var tenantId = Guid.NewGuid();
        await SeedExpiredTrialTenantAsync(tenantId);

        var (client, _) = SetupAdmin(tenantId);
        var response = await client.GetAsync($"/api/v1/tenants/{tenantId}/trial");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TrialStatusResponse>();
        body!.IsOnTrial.Should().BeFalse();
        body.HasUsedTrial.Should().BeTrue();
        body.DaysRemaining.Should().BeNull();
        body.TrialPlan.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetTrialStatus_ForNonExistentTenant_Returns404()
    {
        // Auth claim and URL both reference the same (unseeded) tenant ID so tenant
        // authorization passes, then the repository lookup returns null → 404.
        var tenantId = Guid.NewGuid();
        var (client, _) = SetupAdmin(tenantId);

        var response = await client.GetAsync($"/api/v1/tenants/{tenantId}/trial");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetTrialStatus_WithoutAuth_Returns401()
    {
        var tenantId = Guid.NewGuid();
        await SeedTrialingTenantAsync(tenantId);

        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/v1/tenants/{tenantId}/trial");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── PATCH /tenants/{id}/trial — trial extension (admin action) ────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExtendTrial_WhenOnTrial_ExtendsBySpecifiedDays()
    {
        var tenantId = Guid.NewGuid();
        var tenant = await SeedTrialingTenantAsync(tenantId);
        var originalEndsAt = tenant.TrialEndsAt!.Value;

        var (client, _) = SetupAdmin(tenantId);
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/trial",
            new ExtendTrialRequest { AdditionalDays = 7 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TrialStatusResponse>();
        body!.IsOnTrial.Should().BeTrue();
        body.TrialEndsAt.Should().BeCloseTo(originalEndsAt.AddDays(7), TimeSpan.FromSeconds(5));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExtendTrial_WhenNotOnTrial_Returns400()
    {
        var tenantId = Guid.NewGuid();
        await SeedActiveTenantAsync(tenantId);

        var (client, _) = SetupAdmin(tenantId);
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/trial",
            new ExtendTrialRequest { AdditionalDays = 7 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(91)]
    public async Task ExtendTrial_InvalidAdditionalDays_Returns400(int additionalDays)
    {
        var tenantId = Guid.NewGuid();
        await SeedTrialingTenantAsync(tenantId);

        var (client, _) = SetupAdmin(tenantId);
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/trial",
            new ExtendTrialRequest { AdditionalDays = additionalDays });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Trial is not repeatable (AC5) ─────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_TrialEndsAtIsSet_MarkingTrialAsUsed()
    {
        // TrialEndsAt being set (even after expiry) prevents a second trial from starting.
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "OneTrial Corp", Email = "onetrial@example.com" });

        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();
        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var tenant = await repo.GetByIdAsync(body!.Id);

        // TrialEndsAt is always set after creation — it acts as a "trial used" marker.
        tenant!.TrialEndsAt.Should().NotBeNull();

        var trialService = _factory.Services.GetRequiredService<ITrialService>();
        trialService.GetStatus(tenant).HasUsedTrial.Should().BeTrue();
    }

    // ── POST /admin/trials/expire — trial expiry (AC4) ────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminExpire_ProcessesExpiredTrials_DowngradesToFree()
    {
        var tenantId = Guid.NewGuid();
        await SeedExpiredTrialTenantAsync(tenantId);

        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/v1/admin/trials/expire", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProcessTrialsResponse>();
        body!.Processed.Should().Be(1);

        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var updated = await repo.GetByIdAsync(tenantId);
        updated!.Plan.Should().Be("free");
        updated.Status.Should().Be("active");
        updated.TrialPlan.Should().BeNull();
        updated.MaxDomains.Should().Be(3);
        updated.MaxLinksPerDomain.Should().Be(100);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminExpire_ExpiredTrial_SendsExpiredNotification()
    {
        var tenantId = Guid.NewGuid();
        await SeedExpiredTrialTenantAsync(tenantId);
        _factory.TrialNotification.Reset();

        var client = _factory.CreateClient();
        await client.PostAsync("/api/v1/admin/trials/expire", null);

        _factory.TrialNotification.ExpiredNotifications.Should().ContainSingle(t => t.Id == tenantId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminExpire_NoExpiredTrials_Returns0Processed()
    {
        // Only active (non-trialing) tenants exist — no-op.
        var tenantId = Guid.NewGuid();
        await SeedActiveTenantAsync(tenantId);

        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/v1/admin/trials/expire", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProcessTrialsResponse>();
        body!.Processed.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminExpire_ActiveTrialNotExpired_IsNotDowngraded()
    {
        var tenantId = Guid.NewGuid();
        await SeedTrialingTenantAsync(tenantId); // trial ends in the future

        var client = _factory.CreateClient();
        await client.PostAsync("/api/v1/admin/trials/expire", null);

        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var tenant = await repo.GetByIdAsync(tenantId);
        tenant!.Status.Should().Be("trialing"); // unchanged
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminExpire_PreservesTrialEndsAt_ForAudit()
    {
        var tenantId = Guid.NewGuid();
        var tenant = await SeedExpiredTrialTenantAsync(tenantId);
        var originalEndsAt = tenant.TrialEndsAt;

        var client = _factory.CreateClient();
        await client.PostAsync("/api/v1/admin/trials/expire", null);

        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var updated = await repo.GetByIdAsync(tenantId);
        // TrialEndsAt is kept for audit even after expiry.
        updated!.TrialEndsAt.Should().Be(originalEndsAt);
    }

    // ── POST /admin/trials/notify — expiry notifications (AC3) ───────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminNotify_SendsNotificationForTrialExpiringSoon()
    {
        var tenantId = Guid.NewGuid();
        // Seed a trial expiring in exactly 7 days.
        await SeedTrialingTenantWithEndsAtAsync(tenantId, DateTime.UtcNow.AddDays(7).AddMinutes(-30));
        _factory.TrialNotification.Reset();

        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/v1/admin/trials/notify", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.TrialNotification.ExpiringNotifications.Should()
            .ContainSingle(n => n.Tenant.Id == tenantId && n.DaysRemaining == 7);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminNotify_SendsNotificationForTrialExpiring3Days()
    {
        var tenantId = Guid.NewGuid();
        await SeedTrialingTenantWithEndsAtAsync(tenantId, DateTime.UtcNow.AddDays(3).AddMinutes(-30));
        _factory.TrialNotification.Reset();

        var client = _factory.CreateClient();
        await client.PostAsync("/api/v1/admin/trials/notify", null);

        _factory.TrialNotification.ExpiringNotifications.Should()
            .ContainSingle(n => n.Tenant.Id == tenantId && n.DaysRemaining == 3);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminNotify_SendsNotificationForTrialExpiring1Day()
    {
        var tenantId = Guid.NewGuid();
        await SeedTrialingTenantWithEndsAtAsync(tenantId, DateTime.UtcNow.AddDays(1).AddMinutes(-30));
        _factory.TrialNotification.Reset();

        var client = _factory.CreateClient();
        await client.PostAsync("/api/v1/admin/trials/notify", null);

        _factory.TrialNotification.ExpiringNotifications.Should()
            .ContainSingle(n => n.Tenant.Id == tenantId && n.DaysRemaining == 1);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminNotify_DoesNotNotifyAlreadyExpiredTrials()
    {
        var tenantId = Guid.NewGuid();
        await SeedExpiredTrialTenantAsync(tenantId);
        _factory.TrialNotification.Reset();

        var client = _factory.CreateClient();
        await client.PostAsync("/api/v1/admin/trials/notify", null);

        // Expired tenants have Status="trialing" AND TrialEndsAt in the past,
        // but they fall outside the 7/3/1-day notification windows.
        _factory.TrialNotification.ExpiringNotifications.Should()
            .NotContain(n => n.Tenant.Id == tenantId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminNotify_Returns200WithNotifiedCount()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/v1/admin/trials/notify", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProcessTrialsResponse>();
        body.Should().NotBeNull();
    }

    // ── Stripe webhook integration — trial cleared on paid subscription ────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhookSubscriptionCreated_WhenTrialing_ClearsTrialPlan()
    {
        var tenantId = Guid.NewGuid();
        var tenant = await SeedTrialingTenantAsync(tenantId, stripeCustomerId: "cus_trial_clear_001");

        // Simulate Stripe subscription created (paid)
        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var current = (await repo.GetByIdAsync(tenantId))!;
        current.TrialPlan.Should().Be("pro"); // confirm trial is active

        // StripeWebhookService clears TrialPlan when subscription is established.
        await repo.UpdateAsync(current with
        {
            Plan = "pro",
            Status = "active",
            StripeSubscriptionId = "sub_test_001",
            TrialPlan = null,
            UpdatedAt = DateTime.UtcNow
        });

        var updated = await repo.GetByIdAsync(tenantId);
        updated!.TrialPlan.Should().BeNull();
        updated.StripeSubscriptionId.Should().Be("sub_test_001");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<TenantEntity> SeedTrialingTenantAsync(Guid tenantId, string? stripeCustomerId = null)
        => await SeedTrialingTenantWithEndsAtAsync(tenantId, DateTime.UtcNow.AddDays(14), stripeCustomerId);

    private async Task<TenantEntity> SeedTrialingTenantWithEndsAtAsync(
        Guid tenantId, DateTime trialEndsAt, string? stripeCustomerId = null)
    {
        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var entity = new TenantEntity
        {
            Id = tenantId,
            Name = $"Trial Tenant {tenantId}",
            Email = $"{tenantId}@trial.example.com",
            Plan = "pro",
            Status = "trialing",
            MaxDomains = 50,
            MaxLinksPerDomain = 10_000,
            CreatedAt = DateTime.UtcNow,
            StripeCustomerId = stripeCustomerId,
            TrialEndsAt = trialEndsAt,
            TrialPlan = "pro"
        };
        return await repo.CreateAsync(entity);
    }

    private async Task<TenantEntity> SeedExpiredTrialTenantAsync(Guid tenantId)
    {
        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var entity = new TenantEntity
        {
            Id = tenantId,
            Name = $"Expired Trial Tenant {tenantId}",
            Email = $"{tenantId}@expired.example.com",
            Plan = "pro",
            Status = "trialing",
            MaxDomains = 50,
            MaxLinksPerDomain = 10_000,
            CreatedAt = DateTime.UtcNow.AddDays(-15),
            TrialEndsAt = DateTime.UtcNow.AddHours(-1), // already past
            TrialPlan = "pro"
        };
        return await repo.CreateAsync(entity);
    }

    private async Task<TenantEntity> SeedActiveTenantAsync(Guid tenantId)
    {
        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var entity = new TenantEntity
        {
            Id = tenantId,
            Name = $"Active Tenant {tenantId}",
            Email = $"{tenantId}@active.example.com",
            Plan = "free",
            Status = "active",
            MaxDomains = 3,
            MaxLinksPerDomain = 100,
            CreatedAt = DateTime.UtcNow
        };
        return await repo.CreateAsync(entity);
    }

    private (HttpClient Client, Guid TenantId) SetupAdmin(Guid tenantId)
    {
        var principal = TestClaimsProvider.CreatePrincipal(tenantId.ToString(), "owner");
        _factory.Services.GetRequiredService<TestClaimsProvider>().SetClaims(principal);
        return (_factory.CreateClient(), tenantId);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
