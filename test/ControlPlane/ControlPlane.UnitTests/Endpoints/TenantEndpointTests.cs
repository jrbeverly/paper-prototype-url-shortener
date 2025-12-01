using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.UnitTests.Endpoints;

public sealed class TenantEndpointTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();

    // ── CreateTenant ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateTenant_ValidRequest_Returns201WithBody()
    {
        var client = _factory.CreateClient();
        var request = new CreateTenantRequest { Name = "Acme Corp", Email = "admin@acme.com" };

        var response = await client.PostAsJsonAsync("/api/v1/tenants", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().NotBeEmpty();
        body.Name.Should().Be("Acme Corp");
        body.Email.Should().Be("admin@acme.com");
        body.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateTenant_StartsProTrial()
    {
        var client = _factory.CreateClient();
        var request = new CreateTenantRequest { Name = "Trial Corp", Email = "trial@example.com" };

        var response = await client.PostAsJsonAsync("/api/v1/tenants", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();
        body!.Plan.Should().Be("pro");
        body.Status.Should().Be("trialing");
        body.Trial.Should().NotBeNull();
        body.Trial!.IsOnTrial.Should().BeTrue();
        body.Trial.DaysRemaining.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateTenant_ResponseHasLocationHeader()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Header Corp", Email = "hdr@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.OriginalString.Should().Contain("/tenants/");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateTenant_StripeFailure_StillCreates()
    {
        _factory.StripeCustomer.SimulateFailure();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Stripe Fail Corp", Email = "fail@example.com" });

        // Stripe failure must not block tenant creation
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();
        body!.Id.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateTenant_NormalizesEmailToLowercase()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Case Corp", Email = "UPPER@EXAMPLE.COM" });

        var body = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();
        body!.Email.Should().Be("upper@example.com");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateTenant_MissingName_Returns400()
    {
        var client = _factory.CreateClient();
        var request = new { Email = "admin@acme.com" };

        var response = await client.PostAsJsonAsync("/api/v1/tenants", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateTenant_InvalidEmail_Returns400()
    {
        var client = _factory.CreateClient();
        var request = new CreateTenantRequest { Name = "Valid Name", Email = "not-an-email" };

        var response = await client.PostAsJsonAsync("/api/v1/tenants", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateTenant_InvalidPlan_Returns400()
    {
        var client = _factory.CreateClient();
        var request = new CreateTenantRequest { Name = "Valid Name", Email = "valid@example.com", Plan = "platinum" };

        var response = await client.PostAsJsonAsync("/api/v1/tenants", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateTenant_TwoTenants_HaveDifferentIds()
    {
        var client = _factory.CreateClient();

        var r1 = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "First", Email = "first@example.com" });
        var r2 = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Second", Email = "second@example.com" });

        var b1 = await r1.Content.ReadFromJsonAsync<CreateTenantResponse>();
        var b2 = await r2.Content.ReadFromJsonAsync<CreateTenantResponse>();
        b1!.Id.Should().NotBe(b2!.Id);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
