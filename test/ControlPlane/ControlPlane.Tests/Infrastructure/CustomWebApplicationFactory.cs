using Amazon.DynamoDBv2;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ControlPlane.Tests.Infrastructure;

internal sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    /// <summary>Webhook signing secret used in tests. Compute HMAC-SHA256 of "{timestamp}.{payload}" with this key to produce a valid Stripe-Signature header.</summary>
    public const string TestWebhookSecret = "whsec_test_signing_secret_for_unit_tests";

    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly TestDnsVerificationService _dnsVerification = new();
    private readonly TestStripeCustomerService _stripeCustomer = new();
    private readonly TestStripeInvoiceService _stripeInvoice = new();
    private readonly TestStripePlanService _stripePlan = new();
    private readonly TestTrialNotificationService _trialNotification = new();
    private readonly TestPlanChangeNotificationService _planChangeNotification = new();
    private readonly TestBillingService _billing = new();

    public CustomWebApplicationFactory(IAmazonDynamoDB dynamoDb, string tableName)
    {
        _dynamoDb = dynamoDb;
        _tableName = tableName;
    }

    public TestDnsVerificationService DnsVerification => _dnsVerification;
    public TestStripeCustomerService StripeCustomer => _stripeCustomer;
    public TestStripeInvoiceService StripeInvoice => _stripeInvoice;
    public TestStripePlanService StripePlan => _stripePlan;
    public TestTrialNotificationService TrialNotification => _trialNotification;
    public TestPlanChangeNotificationService PlanChangeNotification => _planChangeNotification;
    public TestBillingService Billing => _billing;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Empty key: health check reports Stripe as "not_configured" (no real network call).
                // Business-level Stripe services (customer, invoice, plan) are replaced with test doubles.
                ["Stripe:SecretKey"] = "",
                ["Stripe:WebhookSecret"] = TestWebhookSecret
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TestClaimsProvider>();

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                if (options.SchemeMap.TryGetValue(JwtBearerDefaults.AuthenticationScheme, out var scheme))
                {
                    scheme.HandlerType = typeof(TestAuthHandler);
                }
            });

            services.RemoveAll<IAmazonDynamoDB>();
            services.AddSingleton(_dynamoDb);

            services.RemoveAll<IApiKeyRepository>();
            services.AddSingleton<IApiKeyRepository>(_ =>
                new DynamoDbApiKeyRepository(_dynamoDb, _tableName));

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

            services.RemoveAll<IPlanChangeNotificationService>();
            services.AddSingleton<IPlanChangeNotificationService>(_planChangeNotification);

            services.RemoveAll<IBillingService>();
            services.AddSingleton<IBillingService>(_billing);
        });
    }

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
    }
}
