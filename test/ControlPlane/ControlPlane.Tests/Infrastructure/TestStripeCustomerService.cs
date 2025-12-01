using ControlPlane.Api.Services;

namespace ControlPlane.Tests.Infrastructure;

/// <summary>In-process fake for IStripeCustomerService. Records invocations and returns a configurable customer ID.</summary>
public sealed class TestStripeCustomerService : IStripeCustomerService
{
    public const string DefaultCustomerId = "cus_test_12345";

    private volatile bool _shouldFail;

    /// <summary>All tenants for which CreateCustomerAsync was called, in call order.</summary>
    public List<TenantEntity> CreatedCustomers { get; } = [];

    /// <summary>Makes the next (and all subsequent) calls return null, simulating a Stripe API failure.</summary>
    public void SimulateFailure() => _shouldFail = true;

    /// <summary>Restores normal (successful) behaviour.</summary>
    public void SimulateSuccess() => _shouldFail = false;

    public Task<string?> CreateCustomerAsync(TenantEntity tenant, CancellationToken cancellationToken = default)
    {
        CreatedCustomers.Add(tenant);
        string? result = _shouldFail ? null : DefaultCustomerId;
        return Task.FromResult(result);
    }
}
