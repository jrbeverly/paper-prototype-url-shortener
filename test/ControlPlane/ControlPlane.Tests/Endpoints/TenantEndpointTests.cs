using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class TenantEndpointTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;

    public TenantEndpointTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    // ── CreateTenant ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_ValidRequest_Returns201WithTenantObject()
    {
        var client = _factory.CreateClient();

        var request = new CreateTenantRequest
        {
            Name = "Acme Corp",
            Email = "admin@acme.com"
        };

        var response = await client.PostAsJsonAsync("/api/v1/tenants", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().NotBeEmpty();
        body.Name.Should().Be("Acme Corp");
        body.Email.Should().Be("admin@acme.com");
        // All new tenants start on a Pro trial; they will be downgraded to Free after 14 days.
        body.Plan.Should().Be("pro");
        body.Status.Should().Be("trialing");
        body.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_ValidRequest_TenantIdIsUnique()
    {
        var client = _factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "First Corp", Email = "first@example.com" });
        var second = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Second Corp", Email = "second@example.com" });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var firstBody = await first.Content.ReadFromJsonAsync<CreateTenantResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<CreateTenantResponse>();

        firstBody!.Id.Should().NotBe(secondBody!.Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_ResponseContainsLocationHeader()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Location Corp", Email = "loc@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.OriginalString.Should().Contain("/tenants/");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_DefaultPlan_PopulatesProTrialLimits()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Free Corp", Email = "free@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();
        // All tenants start on a Pro trial — limits reflect Pro plan.
        body!.Plan.Should().Be("pro");
        body.Status.Should().Be("trialing");
        body.Limits.MaxDomains.Should().Be(50);
        body.Limits.MaxLinksPerDomain.Should().Be(10_000);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_AlwaysStartsOnProTrial_RegardlessOfPlanParam()
    {
        var client = _factory.CreateClient();

        // Tenant requesting starter still starts on Pro trial.
        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Starter Corp", Email = "starter@example.com", Plan = "starter" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();
        body!.Plan.Should().Be("pro");
        body.Status.Should().Be("trialing");
        body.Limits.MaxDomains.Should().Be(50);
        body.Limits.MaxLinksPerDomain.Should().Be(10_000);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_EmailNormalized_StoredAsLowercase()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Case Corp", Email = "Admin@Example.COM" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();
        body!.Email.Should().Be("admin@example.com");
    }

    // ── Validation ───────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("", "admin@example.com")]
    [InlineData("A", "admin@example.com")]
    public async Task CreateTenant_InvalidName_Returns400(string name, string email)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = name, Email = email });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_NameExceedingMaxLength_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = new string('a', 101), Email = "valid@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("not-an-email")]
    [InlineData("missing-at-sign")]
    [InlineData("@nodomain.com")]
    public async Task CreateTenant_InvalidEmail_Returns400(string email)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Valid Corp", Email = email });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("premium")]
    [InlineData("basic")]
    [InlineData("INVALID")]
    public async Task CreateTenant_InvalidPlan_Returns400(string plan)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Valid Corp", Email = "valid@example.com", Plan = plan });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Stripe integration ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_ValidRequest_StripeCustomerIsCreated()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Stripe Corp", Email = "billing@stripe-corp.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();

        // Stripe service should have been called for this tenant.
        _factory.StripeCustomer.CreatedCustomers.Should().ContainSingle(t => t.Id == body!.Id);
        _factory.StripeCustomer.CreatedCustomers[0].Email.Should().Be("billing@stripe-corp.com");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_ValidRequest_StripeCustomerIdStoredOnTenant()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Persistent Corp", Email = "persist@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();

        // Customer ID must be persisted in the repository.
        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var tenant = await repo.GetByIdAsync(body!.Id);
        tenant!.StripeCustomerId.Should().Be(TestStripeCustomerService.DefaultCustomerId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_StripeFailure_StillReturns201WithTenantCreated()
    {
        _factory.StripeCustomer.SimulateFailure();
        try
        {
            var client = _factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/v1/tenants",
                new CreateTenantRequest { Name = "Resilient Corp", Email = "resilient@example.com" });

            // Signup succeeds even when Stripe is unavailable.
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();
            body!.Id.Should().NotBeEmpty();

            // Stripe customer ID is null because creation failed.
            var repo = _factory.Services.GetRequiredService<ITenantRepository>();
            var tenant = await repo.GetByIdAsync(body.Id);
            tenant!.StripeCustomerId.Should().BeNull();
        }
        finally
        {
            _factory.StripeCustomer.SimulateSuccess();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_StripeCustomerCreatedWithIdempotencyKey_TenantIdIsUsed()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Idempotent Corp", Email = "idempotent@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();

        // The Stripe service was called with the tenant entity containing the correct ID,
        // which the production StripeCustomerService uses to build the idempotency key.
        var called = _factory.StripeCustomer.CreatedCustomers.Should().ContainSingle(t => t.Id == body!.Id).Which;
        called.Id.Should().NotBeEmpty();
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
