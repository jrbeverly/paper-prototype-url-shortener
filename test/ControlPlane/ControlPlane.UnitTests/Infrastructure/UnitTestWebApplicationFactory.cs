using Amazon.DynamoDBv2;
using ControlPlane.Api.Authorization;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ControlPlane.UnitTests.Infrastructure;

/// <summary>
/// Lightweight WebApplicationFactory for unit tests. Uses in-memory repositories (Program.cs defaults) and
/// replaces all external service calls with test doubles. No DynamoDB or network required.
/// </summary>
internal class UnitTestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly TestAmazonDynamoDB _dynamoDb = new();
    private readonly TestDnsVerificationService _dnsVerification = new();
    private readonly TestStripeCustomerService _stripeCustomer = new();
    private readonly TestStripeInvoiceService _stripeInvoice = new();
    private readonly TestStripePlanService _stripePlan = new();
    private readonly TestTrialNotificationService _trialNotification = new();
    private readonly TestUsageNotificationService _usageNotification = new();
    private readonly TestUrlSafetyService _urlSafety = new();
    private readonly TestHttpConnectivityCheck _httpConnectivity = new();
    private readonly TestDnsPropagationCheck _dnsPropagation = new();

    public TestAmazonDynamoDB DynamoDb => _dynamoDb;
    public TestDnsVerificationService DnsVerification => _dnsVerification;
    public TestStripeCustomerService StripeCustomer => _stripeCustomer;
    public TestStripeInvoiceService StripeInvoice => _stripeInvoice;
    public TestStripePlanService StripePlan => _stripePlan;
    public TestTrialNotificationService TrialNotification => _trialNotification;
    public TestUsageNotificationService UsageNotification => _usageNotification;
    public TestUrlSafetyService UrlSafety => _urlSafety;
    public TestHttpConnectivityCheck HttpConnectivity => _httpConnectivity;
    public TestDnsPropagationCheck DnsPropagation => _dnsPropagation;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Empty key: health check reports Stripe as "not_configured" (no network calls).
                ["Stripe:SecretKey"] = "",
                ["Stripe:WebhookSecret"] = "whsec_test_unit_test_secret"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Inject TestClaimsProvider and wire it to the JWT handler slot.
            services.AddSingleton<TestClaimsProvider>();

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                if (options.SchemeMap.TryGetValue(JwtBearerDefaults.AuthenticationScheme, out var scheme))
                    scheme.HandlerType = typeof(TestAuthHandler);
            });

            // Replace IAmazonDynamoDB so health checks succeed without a real DynamoDB endpoint.
            services.RemoveAll<IAmazonDynamoDB>();
            services.AddSingleton<IAmazonDynamoDB>(_dynamoDb);

            // Replace external services with test doubles.
            // In-memory repositories (ILinkRepository, IDomainRepository, ITenantRepository,
            // IApiKeyRepository) are kept as-is — they are the Program.cs defaults.
            services.RemoveAll<IDnsVerificationService>();
            services.AddSingleton<IDnsVerificationService>(_dnsVerification);

            services.RemoveAll<IStripeCustomerService>();
            services.AddSingleton<IStripeCustomerService>(_stripeCustomer);

            services.RemoveAll<IInvoiceService>();
            services.AddSingleton<IInvoiceService>(_stripeInvoice);

            services.RemoveAll<IStripePlanService>();
            services.AddSingleton<IStripePlanService>(_stripePlan);

            services.RemoveAll<ITrialNotificationService>();
            services.AddSingleton<ITrialNotificationService>(_trialNotification);

            services.RemoveAll<IUsageNotificationService>();
            services.AddSingleton<IUsageNotificationService>(_usageNotification);

            services.RemoveAll<IUrlSafetyService>();
            services.AddSingleton<IUrlSafetyService>(_urlSafety);

            services.RemoveAll<IHttpConnectivityCheck>();
            services.AddSingleton<IHttpConnectivityCheck>(_httpConnectivity);

            services.RemoveAll<IDnsPropagationCheck>();
            services.AddSingleton<IDnsPropagationCheck>(_dnsPropagation);
        });
    }

    public (HttpClient Client, TestClaimsProvider ClaimsProvider) CreateAuthenticatedClient(
        string tenantId, string role)
    {
        var claimsProvider = Services.GetRequiredService<TestClaimsProvider>();
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(tenantId, role));
        return (CreateClient(), claimsProvider);
    }

    /// <summary>
    /// Pre-seeds a tenant entity in the in-memory repository so plan-aware limit enforcement
    /// uses the specified limits rather than the free-plan fallback.
    /// </summary>
    public async Task SeedTenantAsync(Guid tenantId, string plan = "free", int? maxDomains = null, int? maxLinksPerDomain = null)
    {
        var planDef = PlanCatalog.TryGet(plan) ?? PlanCatalog.TryGet("free")!;
        var repo = Services.GetRequiredService<ITenantRepository>();
        await repo.CreateAsync(new TenantEntity
        {
            Id = tenantId,
            Name = "Test Tenant",
            Email = $"test-{tenantId:N}@example.com",
            Plan = plan,
            Status = "active",
            MaxDomains = maxDomains ?? planDef.MaxDomains,
            MaxLinksPerDomain = maxLinksPerDomain ?? planDef.MaxLinksPerDomain,
            CreatedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Sets the current user to a principal with an additional <c>plan:bypass</c> permission,
    /// allowing limit enforcement to be skipped for testing admin override scenarios.
    /// </summary>
    public void SetPlanBypassClaims(string tenantId, string role)
    {
        var claimsProvider = Services.GetRequiredService<TestClaimsProvider>();
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            tenantId, role, additionalPermissions: [Permissions.PlanBypass]));
    }

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
    }
}
