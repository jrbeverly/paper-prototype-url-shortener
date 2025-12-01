using ControlPlane.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.UnitTests.Services;

public sealed class TrialServiceTests
{
    private readonly FakeTenantRepository _repo = new();
    private readonly TrialOptions _options = new() { DurationDays = 14, TrialPlanId = "pro" };

    private TrialService BuildSut() =>
        new(_repo, Options.Create(_options), NullLogger<TrialService>.Instance);

    private static TenantEntity NewTenant(string status = "active", string plan = "free") => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Tenant",
        Email = "test@example.com",
        Plan = plan,
        Status = status,
        MaxDomains = 3,
        MaxLinksPerDomain = 100,
        CreatedAt = DateTime.UtcNow
    };

    // ── GetStatus ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void GetStatus_NoTrialEverUsed_ReturnsFalseNotOnTrial()
    {
        var tenant = NewTenant();
        var status = BuildSut().GetStatus(tenant);

        status.IsOnTrial.Should().BeFalse();
        status.HasUsedTrial.Should().BeFalse();
        status.DaysRemaining.Should().BeNull();
        status.TrialPlan.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetStatus_ActiveTrial_ReturnsIsOnTrialTrue()
    {
        var tenant = NewTenant("trialing", "pro") with
        {
            TrialEndsAt = DateTime.UtcNow.AddDays(10),
            TrialPlan = "pro"
        };

        var status = BuildSut().GetStatus(tenant);

        status.IsOnTrial.Should().BeTrue();
        status.HasUsedTrial.Should().BeTrue();
        status.DaysRemaining.Should().BeGreaterThan(0);
        status.TrialPlan.Should().Be("pro");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetStatus_ExpiredTrial_IsOnTrialFalse()
    {
        var tenant = NewTenant("active", "free") with
        {
            TrialEndsAt = DateTime.UtcNow.AddDays(-1),
            TrialPlan = null
        };

        var status = BuildSut().GetStatus(tenant);

        status.IsOnTrial.Should().BeFalse();
        status.HasUsedTrial.Should().BeTrue();
        status.DaysRemaining.Should().BeNull();
        status.TrialPlan.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetStatus_ActiveTrial_DaysRemainingRoundedUp()
    {
        // 10.3 days remaining → should report 11
        var tenant = NewTenant("trialing", "pro") with
        {
            TrialEndsAt = DateTime.UtcNow.AddDays(10).AddHours(8),
            TrialPlan = "pro"
        };

        var status = BuildSut().GetStatus(tenant);

        status.DaysRemaining.Should().Be(11);
    }

    // ── StartTrial ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void StartTrial_NewTenant_ActivatesProTrial()
    {
        var tenant = NewTenant();

        var result = BuildSut().StartTrial(tenant);

        result.Status.Should().Be("trialing");
        result.Plan.Should().Be("pro");
        result.TrialPlan.Should().Be("pro");
        result.TrialEndsAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromSeconds(5));
        result.MaxDomains.Should().Be(PlanCatalog.Get("pro").MaxDomains);
        result.MaxLinksPerDomain.Should().Be(PlanCatalog.Get("pro").MaxLinksPerDomain);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StartTrial_AlreadyUsedTrial_IsNoOp()
    {
        var tenant = NewTenant("active", "free") with
        {
            TrialEndsAt = DateTime.UtcNow.AddDays(-5)
        };

        var result = BuildSut().StartTrial(tenant);

        result.Should().Be(tenant);
        result.Status.Should().Be("active");
    }

    // ── ExpireAsync ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExpireAsync_TrialingTenant_DowngradesToFree()
    {
        var tenant = NewTenant("trialing", "pro") with
        {
            TrialEndsAt = DateTime.UtcNow.AddDays(-1),
            TrialPlan = "pro",
            MaxDomains = PlanCatalog.Get("pro").MaxDomains,
            MaxLinksPerDomain = PlanCatalog.Get("pro").MaxLinksPerDomain
        };
        _repo.Seed(tenant);

        var result = await BuildSut().ExpireAsync(tenant);

        result.Plan.Should().Be("free");
        result.Status.Should().Be("active");
        result.TrialPlan.Should().BeNull();
        result.MaxDomains.Should().Be(PlanCatalog.Get("free").MaxDomains);
        result.MaxLinksPerDomain.Should().Be(PlanCatalog.Get("free").MaxLinksPerDomain);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExpireAsync_NonTrialingTenant_IsNoOp()
    {
        var tenant = NewTenant("active", "free");
        _repo.Seed(tenant);

        var result = await BuildSut().ExpireAsync(tenant);

        result.Should().Be(tenant);
        _repo.UpdateCallCount.Should().Be(0);
    }

    // ── ExtendAsync ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExtendAsync_TrialingTenant_ExtendsByGivenDays()
    {
        var originalEnd = DateTime.UtcNow.AddDays(3);
        var tenant = NewTenant("trialing", "pro") with
        {
            TrialEndsAt = originalEnd,
            TrialPlan = "pro"
        };
        _repo.Seed(tenant);

        var result = await BuildSut().ExtendAsync(tenant, 7);

        result.TrialEndsAt.Should().BeCloseTo(originalEnd.AddDays(7), TimeSpan.FromSeconds(2));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExtendAsync_NonTrialingTenant_ThrowsInvalidOperation()
    {
        var tenant = NewTenant("active", "free");
        _repo.Seed(tenant);

        var act = () => BuildSut().ExtendAsync(tenant, 7);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── Fake repository ───────────────────────────────────────────────────────

    private sealed class FakeTenantRepository : ITenantRepository
    {
        private TenantEntity? _stored;

        public int UpdateCallCount { get; private set; }

        public void Seed(TenantEntity tenant) => _stored = tenant;

        public Task<TenantEntity> CreateAsync(TenantEntity entity)
        {
            _stored = entity;
            return Task.FromResult(entity);
        }

        public Task<TenantEntity?> GetByIdAsync(Guid tenantId) =>
            Task.FromResult(_stored?.Id == tenantId ? _stored : null);

        public Task<TenantEntity?> GetByEmailAsync(string email) =>
            Task.FromResult<TenantEntity?>(null);

        public Task<TenantEntity?> GetByStripeCustomerIdAsync(string stripeCustomerId) =>
            Task.FromResult<TenantEntity?>(null);

        public Task<TenantEntity> UpdateAsync(TenantEntity entity)
        {
            UpdateCallCount++;
            _stored = entity;
            return Task.FromResult(entity);
        }

        public Task<IReadOnlyList<TenantEntity>> GetExpiringTrialsAsync(
            DateTime from, DateTime to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantEntity>>([]);

        public Task<(IReadOnlyList<TenantEntity> Items, int TotalCount)> ListAsync(
            string? status = null, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
            Task.FromResult<(IReadOnlyList<TenantEntity>, int)>(([], 0));
    }
}
