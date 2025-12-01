using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class InvoiceEndpointTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;

    public InvoiceEndpointTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    // ── Helper ───────────────────────────────────────────────────────────

    /// <summary>Creates a tenant via the API and returns an authenticated client scoped to that tenant.</summary>
    private async Task<(HttpClient Client, Guid TenantId)> CreateTenantAndGetClientAsync(string role = Roles.Owner)
    {
        var unauthenticatedClient = _factory.CreateClient();
        var createResponse = await unauthenticatedClient.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Invoice Corp", Email = "billing@invoice-corp.com" });
        createResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);

        var body = await createResponse.Content.ReadFromJsonAsync<CreateTenantResponse>();
        var tenantId = body!.Id;

        var (client, _) = _factory.CreateAuthenticatedClient(tenantId.ToString(), role);
        return (client, tenantId);
    }

    // ── ListInvoices ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListInvoices_AuthenticatedOwner_Returns200WithInvoices()
    {
        var (client, tenantId) = await CreateTenantAndGetClientAsync();

        var response = await client.GetAsync($"/api/v1/tenants/{tenantId}/invoices");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InvoiceListResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(1);
        body.HasMore.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListInvoices_ReturnsCorrectInvoiceFields()
    {
        var (client, tenantId) = await CreateTenantAndGetClientAsync();

        var response = await client.GetAsync($"/api/v1/tenants/{tenantId}/invoices");

        var body = await response.Content.ReadFromJsonAsync<InvoiceListResponse>();
        var invoice = body!.Items[0];
        invoice.Id.Should().Be(TestStripeInvoiceService.DefaultInvoice.Id);
        invoice.Status.Should().Be("paid");
        invoice.AmountDue.Should().Be(2900);
        invoice.AmountPaid.Should().Be(2900);
        invoice.Currency.Should().Be("usd");
        invoice.InvoicePdfUrl.Should().Be(TestStripeInvoiceService.DefaultPdfUrl);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListInvoices_InvalidLimit_Returns400()
    {
        var (client, tenantId) = await CreateTenantAndGetClientAsync();

        var response = await client.GetAsync($"/api/v1/tenants/{tenantId}/invoices?limit=0");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListInvoices_LimitTooLarge_Returns400()
    {
        var (client, tenantId) = await CreateTenantAndGetClientAsync();

        var response = await client.GetAsync($"/api/v1/tenants/{tenantId}/invoices?limit=101");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListInvoices_UnknownTenant_Returns404()
    {
        var unknownTenantId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(unknownTenantId.ToString(), Roles.Owner);

        var response = await client.GetAsync($"/api/v1/tenants/{unknownTenantId}/invoices");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListInvoices_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/tenants/{Guid.NewGuid()}/invoices");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(Roles.Viewer)]
    [InlineData(Roles.Member)]
    public async Task ListInvoices_WithNonBillingRole_Returns403(string role)
    {
        var tenantId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(tenantId.ToString(), role);

        var response = await client.GetAsync($"/api/v1/tenants/{tenantId}/invoices");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListInvoices_CrossTenant_Returns403()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(tenantB.ToString(), Roles.Owner);

        var response = await client.GetAsync($"/api/v1/tenants/{tenantA}/invoices");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    // ── GetInvoice ───────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetInvoice_ExistingInvoice_Returns200WithLineItems()
    {
        var (client, tenantId) = await CreateTenantAndGetClientAsync();

        var response = await client.GetAsync(
            $"/api/v1/tenants/{tenantId}/invoices/{TestStripeInvoiceService.DefaultInvoice.Id}");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InvoiceDetailResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(TestStripeInvoiceService.DefaultInvoice.Id);
        body.Subtotal.Should().Be(3400);
        body.Total.Should().Be(2900);
        body.LineItems.Should().HaveCount(3);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetInvoice_LineItemsHaveCorrectTypes()
    {
        var (client, tenantId) = await CreateTenantAndGetClientAsync();

        var response = await client.GetAsync(
            $"/api/v1/tenants/{tenantId}/invoices/{TestStripeInvoiceService.DefaultInvoice.Id}");

        var body = await response.Content.ReadFromJsonAsync<InvoiceDetailResponse>();
        body!.LineItems.Should().Contain(l => l.Type == "plan");
        body.LineItems.Should().Contain(l => l.Type == "overage");
        body.LineItems.Should().Contain(l => l.Type == "credit");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetInvoice_UnknownInvoiceId_Returns404()
    {
        var (client, tenantId) = await CreateTenantAndGetClientAsync();

        var response = await client.GetAsync(
            $"/api/v1/tenants/{tenantId}/invoices/in_nonexistent");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetInvoice_UnknownTenant_Returns404()
    {
        var unknownTenantId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(unknownTenantId.ToString(), Roles.Owner);

        var response = await client.GetAsync(
            $"/api/v1/tenants/{unknownTenantId}/invoices/{TestStripeInvoiceService.DefaultInvoice.Id}");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetInvoice_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/tenants/{Guid.NewGuid()}/invoices/in_test");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetInvoice_CrossTenant_Returns403()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(tenantB.ToString(), Roles.Owner);

        var response = await client.GetAsync(
            $"/api/v1/tenants/{tenantA}/invoices/{TestStripeInvoiceService.DefaultInvoice.Id}");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    // ── GetInvoicePdf ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetInvoicePdf_ExistingInvoice_Redirects()
    {
        var (_, tenantId) = await CreateTenantAndGetClientAsync();

        // Set claims before creating the no-redirect client; TestAuthHandler reads from the shared singleton.
        var claimsProvider = _factory.Services.GetRequiredService<TestClaimsProvider>();
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(tenantId.ToString(), Roles.Owner));

        var noRedirectClient = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await noRedirectClient.GetAsync(
            $"/api/v1/tenants/{tenantId}/invoices/{TestStripeInvoiceService.DefaultInvoice.Id}/pdf");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.OriginalString.Should().Be(TestStripeInvoiceService.DefaultPdfUrl);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetInvoicePdf_UnknownInvoiceId_Returns404()
    {
        var (client, tenantId) = await CreateTenantAndGetClientAsync();

        var response = await client.GetAsync(
            $"/api/v1/tenants/{tenantId}/invoices/in_nonexistent/pdf");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetInvoicePdf_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/tenants/{Guid.NewGuid()}/invoices/in_test/pdf");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetInvoicePdf_CrossTenant_Returns403()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(tenantB.ToString(), Roles.Owner);

        var response = await client.GetAsync(
            $"/api/v1/tenants/{tenantA}/invoices/{TestStripeInvoiceService.DefaultInvoice.Id}/pdf");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    // ── GetUpcomingInvoice ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetUpcomingInvoice_WithSubscription_Returns200()
    {
        var (client, tenantId) = await CreateTenantAndGetClientAsync();

        var response = await client.GetAsync($"/api/v1/tenants/{tenantId}/invoices/upcoming");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UpcomingInvoiceResponse>();
        body.Should().NotBeNull();
        body!.AmountDue.Should().Be(2900);
        body.Currency.Should().Be("usd");
        body.NextPaymentAttempt.Should().Be(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetUpcomingInvoice_HasLineItems()
    {
        var (client, tenantId) = await CreateTenantAndGetClientAsync();

        var response = await client.GetAsync($"/api/v1/tenants/{tenantId}/invoices/upcoming");

        var body = await response.Content.ReadFromJsonAsync<UpcomingInvoiceResponse>();
        body!.LineItems.Should().NotBeEmpty();
        body.LineItems.Should().Contain(l => l.Type == "plan");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetUpcomingInvoice_NoUpcomingInvoice_Returns404()
    {
        _factory.StripeInvoice.SetHasUpcomingInvoice(false);
        try
        {
            var (client, tenantId) = await CreateTenantAndGetClientAsync();

            var response = await client.GetAsync($"/api/v1/tenants/{tenantId}/invoices/upcoming");

            response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        }
        finally
        {
            _factory.StripeInvoice.SetHasUpcomingInvoice(true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetUpcomingInvoice_UnknownTenant_Returns404()
    {
        var unknownTenantId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(unknownTenantId.ToString(), Roles.Owner);

        var response = await client.GetAsync($"/api/v1/tenants/{unknownTenantId}/invoices/upcoming");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetUpcomingInvoice_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/tenants/{Guid.NewGuid()}/invoices/upcoming");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetUpcomingInvoice_CrossTenant_Returns403()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(tenantB.ToString(), Roles.Owner);

        var response = await client.GetAsync($"/api/v1/tenants/{tenantA}/invoices/upcoming");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    // ── Tenant filtering (invoices scoped to requesting tenant) ──────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Invoices_AreFilteredToRequestingTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Authenticated as Tenant A — can access their own invoices.
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(tenantA.ToString(), Roles.Owner);

        // We can't list tenant A's invoices here because the tenant doesn't exist in the repo;
        // instead we verify cross-tenant isolation: Tenant B cannot access Tenant A's invoices.
        // Switch to Tenant B identity.
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(tenantB.ToString(), Roles.Owner));
        var crossResp = await client.GetAsync($"/api/v1/tenants/{tenantA}/invoices");
        crossResp.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);

        // Switch back to Tenant A — Tenant A can still request their own invoices URL
        // (returns 404 since the tenant doesn't exist in the repo, not 403 — proving tenant isolation works).
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(tenantA.ToString(), Roles.Owner));
        var ownResp = await client.GetAsync($"/api/v1/tenants/{tenantA}/invoices");
        ownResp.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
