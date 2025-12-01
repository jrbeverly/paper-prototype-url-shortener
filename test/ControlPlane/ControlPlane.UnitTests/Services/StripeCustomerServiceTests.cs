using ControlPlane.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stripe;

namespace ControlPlane.UnitTests.Services;

/// <summary>
/// Unit tests for StripeCustomerService: successful creation, Stripe failure handling, and idempotency.
/// </summary>
public sealed class StripeCustomerServiceTests
{
    private readonly Mock<CustomerService> _customerService;
    private readonly StripeCustomerService _sut;

    private static readonly TenantEntity _defaultTenant = new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Corp",
        Email = "test@example.com",
        Plan = "pro",
        Status = "active",
        MaxDomains = 50,
        MaxLinksPerDomain = 10_000,
        CreatedAt = DateTime.UtcNow
    };

    public StripeCustomerServiceTests()
    {
        _customerService = new Mock<CustomerService>(new StripeClient("sk_test_dummy"));
        _sut = new StripeCustomerService(_customerService.Object, NullLogger<StripeCustomerService>.Instance);
    }

    // ── Success ─────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateCustomerAsync_Success_ReturnsCustomerId()
    {
        _customerService
            .Setup(s => s.CreateAsync(
                It.IsAny<CustomerCreateOptions>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { Id = "cus_test_abc123" });

        var result = await _sut.CreateCustomerAsync(_defaultTenant);

        result.Should().Be("cus_test_abc123");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateCustomerAsync_PassesTenantNameAndEmail()
    {
        CustomerCreateOptions? captured = null;
        _customerService
            .Setup(s => s.CreateAsync(
                It.IsAny<CustomerCreateOptions>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<CustomerCreateOptions, RequestOptions, CancellationToken>((opts, _, _) => captured = opts)
            .ReturnsAsync(new Customer { Id = "cus_test" });

        await _sut.CreateCustomerAsync(_defaultTenant);

        captured.Should().NotBeNull();
        captured!.Name.Should().Be(_defaultTenant.Name);
        captured.Email.Should().Be(_defaultTenant.Email);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateCustomerAsync_SetsTenantIdInMetadata()
    {
        CustomerCreateOptions? captured = null;
        _customerService
            .Setup(s => s.CreateAsync(
                It.IsAny<CustomerCreateOptions>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<CustomerCreateOptions, RequestOptions, CancellationToken>((opts, _, _) => captured = opts)
            .ReturnsAsync(new Customer { Id = "cus_test" });

        await _sut.CreateCustomerAsync(_defaultTenant);

        captured!.Metadata.Should().ContainKey("tenant_id");
        captured.Metadata["tenant_id"].Should().Be(_defaultTenant.Id.ToString());
        captured.Metadata.Should().ContainKey("plan");
        captured.Metadata["plan"].Should().Be("pro");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateCustomerAsync_UsesIdempotencyKey()
    {
        RequestOptions? captured = null;
        _customerService
            .Setup(s => s.CreateAsync(
                It.IsAny<CustomerCreateOptions>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<CustomerCreateOptions, RequestOptions, CancellationToken>((_, req, _) => captured = req)
            .ReturnsAsync(new Customer { Id = "cus_test" });

        await _sut.CreateCustomerAsync(_defaultTenant);

        captured.Should().NotBeNull();
        captured!.IdempotencyKey.Should().Be($"tenant-create-{_defaultTenant.Id}");
    }

    // ── Failure ─────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateCustomerAsync_StripeException_ReturnsNull()
    {
        _customerService
            .Setup(s => s.CreateAsync(
                It.IsAny<CustomerCreateOptions>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException(System.Net.HttpStatusCode.InternalServerError,
                new StripeError { Message = "API error" }, "Test error"));

        var result = await _sut.CreateCustomerAsync(_defaultTenant);

        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateCustomerAsync_StripeException_DoesNotThrow()
    {
        _customerService
            .Setup(s => s.CreateAsync(
                It.IsAny<CustomerCreateOptions>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException(System.Net.HttpStatusCode.ServiceUnavailable,
                new StripeError { Message = "Service unavailable" }, "Test error"));

        var act = () => _sut.CreateCustomerAsync(_defaultTenant);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateCustomerAsync_NetworkError_ReturnsNull()
    {
        _customerService
            .Setup(s => s.CreateAsync(
                It.IsAny<CustomerCreateOptions>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException(System.Net.HttpStatusCode.RequestTimeout,
                new StripeError { Message = "Network timeout" }, "Timeout"));

        var result = await _sut.CreateCustomerAsync(_defaultTenant);

        result.Should().BeNull();
    }
}
