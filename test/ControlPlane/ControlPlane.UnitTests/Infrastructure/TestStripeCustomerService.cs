using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Infrastructure;

public sealed class TestStripeCustomerService : IStripeCustomerService
{
    public const string DefaultCustomerId = "cus_test_12345";

    private volatile bool _shouldFail;

    public List<TenantEntity> CreatedCustomers { get; } = [];

    public void SimulateFailure() => _shouldFail = true;
    public void SimulateSuccess() => _shouldFail = false;

    public Task<string?> CreateCustomerAsync(TenantEntity tenant, CancellationToken cancellationToken = default)
    {
        CreatedCustomers.Add(tenant);
        string? result = _shouldFail ? null : DefaultCustomerId;
        return Task.FromResult(result);
    }
}
