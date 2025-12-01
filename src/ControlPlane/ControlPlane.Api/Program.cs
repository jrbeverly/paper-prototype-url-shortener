using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.CloudFront;
using Amazon.DynamoDBv2;
using Amazon.XRay.Recorder.Handlers.AspNetCore;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using Common.ErrorHandling;
using ControlPlane.Api.Authentication;
using ControlPlane.Api.Endpoints;
using ControlPlane.Api.Endpoints.Webhooks;
using ControlPlane.Api.Extensions;
using ControlPlane.Api.Middleware;
using ControlPlane.Api.Services;
using ControlPlane.Api.Versioning;
using DnsClient;
using FluentValidation;
using LinkCfg = ControlPlane.Api.Services.LinkOptions;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ── X-Ray SDK ─────────────────────────────────────────────────────────────
// Instrument all AWS SDK calls (DynamoDB, SES, etc.) with X-Ray subsegments.
// Called before any AWS SDK client instances are created so the pipeline is
// patched before the first DynamoDB client is constructed.
// Skipped in Development/Test where no X-Ray daemon runs.
if (builder.Environment.IsProduction() || builder.Environment.IsStaging())
{
    AWSSDKHandler.RegisterXRayForAllServices();
}

// ── Logging ───────────────────────────────────────────────────────────────
// Emit structured JSON to stdout so CloudWatch Logs Insights can query individual fields.
// IncludeScopes=true ensures CorrelationId (set per-request by CorrelationIdMiddleware)
// appears in every log entry within the request scope.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fff";
});

// ── API Versioning configuration ────────────────────────────────────────
builder.Services.Configure<ApiVersionConfig>(
    builder.Configuration.GetSection(ApiVersionConfig.SectionName));

var versionConfig = builder.Configuration
    .GetSection(ApiVersionConfig.SectionName)
    .Get<ApiVersionConfig>() ?? new ApiVersionConfig { CurrentMajorVersion = 1 };

// ── JSON serialization ────────────────────────────────────────────────
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

// ── OpenAPI / Swagger ─────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc($"v{versionConfig.CurrentMajorVersion}", new()
    {
        Title = "ControlPlane API",
        Version = $"v{versionConfig.CurrentMajorVersion}",
        Description = "Management API for tenants, domains, links, and API keys."
    });

    // Include XML documentation from endpoint handlers and DTOs
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);

    // Bearer JWT
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    // API Key
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "API Key authentication using the X-API-Key header.",
        Name = "X-API-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });

    options.OperationFilter<SecurityRequirementsOperationFilter>();

    options.CustomSchemaIds(type => type.FullName?.Replace('+', '.'));
});

// ── Authentication & Authorization ─────────────────────────────────────
builder.Services.AddAppAuthentication(builder.Configuration);

// ── Validation ────────────────────────────────────────────────────────
builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

// ── In-process cache (used by FeatureFlagService and other caching concerns) ──
builder.Services.AddMemoryCache();

// ── Rate limiting ─────────────────────────────────────────────────────────
builder.Services.Configure<RateLimitOptions>(
    builder.Configuration.GetSection(RateLimitOptions.SectionName));
builder.Services.AddSingleton<ILinkRateLimiter, InMemoryLinkRateLimiter>();

// ── Repository ─────────────────────────────────────────────────────────
builder.Services.Configure<DomainOptions>(
    builder.Configuration.GetSection(DomainOptions.SectionName));
builder.Services.Configure<LinkCfg>(
    builder.Configuration.GetSection(LinkCfg.SectionName));
builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder.Services.AddSingleton<ITenantRepository, InMemoryTenantRepository>();
builder.Services.AddSingleton<IApiKeyRepository, InMemoryApiKeyRepository>();
builder.Services.AddSingleton<IDomainRepository, InMemoryDomainRepository>();
builder.Services.AddSingleton<ILinkRepository, InMemoryLinkRepository>();
builder.Services.AddSingleton<IBulkLinkService, BulkLinkService>();
builder.Services.AddSingleton<IAnalyticsService, InMemoryAnalyticsService>();
builder.Services.AddSingleton<ILookupClient>(_ => new LookupClient());
builder.Services.AddSingleton<IDnsVerificationService, DnsVerificationService>();
builder.Services.Configure<DomainValidationOptions>(
    builder.Configuration.GetSection(DomainValidationOptions.SectionName));
builder.Services.AddSingleton<IDomainValidationService, DomainValidationService>();
builder.Services.AddSingleton<IHealthCheckService, HealthCheckService>();

// ── Stripe ─────────────────────────────────────────────────────────────
// SecretKey and WebhookSecret are populated at runtime from AWS Secrets Manager
// via environment variables STRIPE__SECRETKEY and STRIPE__WEBHOOKSECRET.
builder.Services.Configure<StripeOptions>(
    builder.Configuration.GetSection(StripeOptions.SectionName));
builder.Services.AddSingleton<IStripeCustomerService, StripeCustomerService>();
builder.Services.AddSingleton<IInvoiceService, StripeInvoiceService>();
builder.Services.AddSingleton<IWebhookEventStore, InMemoryWebhookEventStore>();
builder.Services.AddSingleton<IStripeWebhookService, StripeWebhookService>();
builder.Services.AddSingleton<IStripePlanService, StripePlanService>();

// ── Trial management ───────────────────────────────────────────────────
builder.Services.Configure<TrialOptions>(
    builder.Configuration.GetSection(TrialOptions.SectionName));
builder.Services.AddSingleton<ITrialService, TrialService>();
builder.Services.AddSingleton<ITrialNotificationService, NoOpTrialNotificationService>();

// ── Audit log ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IAuditLogService, InMemoryAuditLogService>();

// ── Usage metering ────────────────────────────────────────────────────────
builder.Services.AddSingleton<IUsageRepository, InMemoryUsageRepository>();
builder.Services.AddSingleton<IUsageAlertService, InMemoryUsageAlertService>();
builder.Services.AddSingleton<IUsageNotificationService, NoOpUsageNotificationService>();
builder.Services.AddSingleton<IStripeUsageReportingService, NoOpStripeUsageReportingService>();
builder.Services.AddSingleton<IUsageService, UsageService>();

// ── Feature flags ─────────────────────────────────────────────────────────
builder.Services.Configure<FeatureFlagOptions>(
    builder.Configuration.GetSection(FeatureFlagOptions.SectionName));
builder.Services.AddSingleton<IFeatureFlagRepository, InMemoryFeatureFlagRepository>();
builder.Services.AddSingleton<IFeatureFlagService, FeatureFlagService>();

// ── URL safety scanning ────────────────────────────────────────────────────
builder.Services.Configure<UrlSafetyOptions>(
    builder.Configuration.GetSection(UrlSafetyOptions.SectionName));
builder.Services.AddSingleton<IUrlSafetyService, InMemoryUrlSafetyService>();

// ── Periodic destination re-scanning ──────────────────────────────────────────
builder.Services.Configure<LinkReScanOptions>(
    builder.Configuration.GetSection(LinkReScanOptions.SectionName));
builder.Services.AddHostedService<LinkReScanService>();

// ── DNS verification polling ───────────────────────────────────────────────────
builder.Services.Configure<DnsPollingOptions>(
    builder.Configuration.GetSection(DnsPollingOptions.SectionName));
builder.Services.AddSingleton<IDomainStatusNotificationService, NoOpDomainStatusNotificationService>();
builder.Services.AddSingleton<IDomainActivationService, DomainActivationService>();
builder.Services.AddHostedService<DnsPollingService>();

// ── Certificate provisioning ───────────────────────────────────────────────
builder.Services.Configure<CertificateOptions>(
    builder.Configuration.GetSection(CertificateOptions.SectionName));
builder.Services.AddSingleton<ICertificateService, InMemoryCertificateService>();

// ── Domain diagnostics ─────────────────────────────────────────────────────
// Named "diagnostics" client: auto-redirect off so status codes are reported as-received.
builder.Services.AddHttpClient("diagnostics")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<IHttpConnectivityCheck, HttpConnectivityCheck>();
builder.Services.AddSingleton<IDnsPropagationCheck, DnsPropagationCheck>();
builder.Services.AddSingleton<IDomainDiagnosticsService, DomainDiagnosticsService>();

// ── CloudFront distribution tenant lifecycle ───────────────────────────────
builder.Services.Configure<DistributionOptions>(
    builder.Configuration.GetSection(DistributionOptions.SectionName));
builder.Services.AddSingleton<IAmazonCloudFront>(_ => new AmazonCloudFrontClient());
builder.Services.AddSingleton<IDistributionService, InMemoryDistributionService>();

// ── Billing (plan change workflow) ────────────────────────────────────────
builder.Services.AddSingleton<IPlanChangeNotificationService, NoOpPlanChangeNotificationService>();
builder.Services.AddSingleton<IBillingService, StripeBillingService>();

// ── CORS ──────────────────────────────────────────────────────────────
var corsOrigins = builder.Configuration
    .GetSection("Cors:Origins")
    .Get<string[]>() ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// ── Swagger UI (development only) ─────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            $"/swagger/v{versionConfig.CurrentMajorVersion}/swagger.json",
            $"ControlPlane API v{versionConfig.CurrentMajorVersion}");
    });
}

// ── Middleware pipeline ───────────────────────────────────────────────
// Correlation ID first: opens the log scope so all downstream middleware and handlers
// emit log entries with CorrelationId (and TraceId when X-Ray is active) in the JSON output.
app.UseMiddleware<CorrelationIdMiddleware>();
// X-Ray second: creates a segment for the full request lifetime so all downstream
// subsegments (DynamoDB calls) are nested inside the request segment.
if (app.Environment.IsProduction() || app.Environment.IsStaging())
{
    app.UseXRay("ControlPlane");
}
app.UseMiddleware<DeprecationHeaderMiddleware>();
app.UseMiddleware<DomainExceptionMiddleware>();
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// ── Unversioned endpoints (operational) ───────────────────────────────
HealthEndpoints.Map(app);
StripeWebhookEndpoints.Map(app);

// ── Versioned endpoint auto-discovery ─────────────────────────────────
var v1 = app.MapGroup(
    $"/api/v{versionConfig.CurrentMajorVersion}");

foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
{
    if (type is { IsClass: true, IsAbstract: false, IsPublic: true } &&
        type.GetInterfaces().Any(i => i == typeof(IEndpointGroup)))
    {
        var method = type.GetMethod("Map", BindingFlags.Public | BindingFlags.Static);
        method?.Invoke(null, [v1]);
    }
}

app.Run();
