using Microsoft.Extensions.Options;
using Stripe;

namespace ControlPlane.Api.Services;

/// <summary>Stripe customer lifecycle management backed by the Stripe API.</summary>
public class StripeCustomerService : IStripeCustomerService
{
    private readonly CustomerService _customerService;
    private readonly ILogger<StripeCustomerService> _logger;

    public StripeCustomerService(IOptions<StripeOptions> options, ILogger<StripeCustomerService> logger)
        : this(new CustomerService(new StripeClient(options.Value.SecretKey)), logger) { }

    internal StripeCustomerService(CustomerService customerService, ILogger<StripeCustomerService> logger)
    {
        _customerService = customerService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> CreateCustomerAsync(TenantEntity tenant, CancellationToken cancellationToken = default)
    {
        var createOptions = new CustomerCreateOptions
        {
            Name = tenant.Name,
            Email = tenant.Email,
            Metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = tenant.Id.ToString(),
                ["plan"] = tenant.Plan
            }
        };

        // Idempotency key ensures duplicate calls (retries, replays) produce the same customer.
        var requestOptions = new RequestOptions
        {
            IdempotencyKey = $"tenant-create-{tenant.Id}"
        };

        try
        {
            var customer = await _customerService.CreateAsync(createOptions, requestOptions, cancellationToken);
            _logger.LogInformation(
                "Stripe customer {CustomerId} created for tenant {TenantId}",
                customer.Id, tenant.Id);
            return customer.Id;
        }
        catch (StripeException ex)
        {
            _logger.LogError(
                ex,
                "Stripe customer creation failed for tenant {TenantId}: {StripeError}",
                tenant.Id, ex.StripeError?.Message ?? ex.Message);
            return null;
        }
    }
}
