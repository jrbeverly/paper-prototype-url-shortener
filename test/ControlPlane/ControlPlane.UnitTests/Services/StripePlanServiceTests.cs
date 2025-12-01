using ControlPlane.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Stripe;

namespace ControlPlane.UnitTests.Services;

/// <summary>
/// Unit tests for StripePlanService: sync, caching, error handling, and plan pricing mapping.
/// </summary>
public sealed class StripePlanServiceTests : IDisposable
{
    private readonly Mock<ProductService> _productService;
    private readonly Mock<PriceService> _priceService;
    private readonly StripePlanService _sut;

    public StripePlanServiceTests()
    {
        var client = new StripeClient("sk_test_dummy");
        _productService = new Mock<ProductService>(client);
        _priceService = new Mock<PriceService>(client);

        SetupAllProductsAsNew();
        SetupAllPricesAsNew();

        var mockSut = new Mock<StripePlanService>(client, NullLogger<StripePlanService>.Instance) { CallBase = true };
        mockSut.Protected().Setup<ProductService>("CreateProductService").Returns(_productService.Object);
        mockSut.Protected().Setup<PriceService>("CreatePriceService").Returns(_priceService.Object);
        _sut = mockSut.Object;
    }

    public void Dispose()
    {
        _sut.SyncAsync().GetAwaiter().GetResult(); // ensure semaphore releases
    }

    // ── GetPricingAsync / SyncAsync ─────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPricingAsync_FirstCall_SyncsAndCaches()
    {
        var result = await _sut.GetPricingAsync();

        result.Should().NotBeNull();
        result.Should().ContainKey("pro");
        result["pro"].ProductId.Should().NotBeNull();
        result["pro"].PriceId.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPricingAsync_FreePlan_HasNoProductOrPrice()
    {
        var result = await _sut.GetPricingAsync();

        result.Should().ContainKey("free");
        result["free"].ProductId.Should().BeNull();
        result["free"].PriceId.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPricingAsync_EnterprisePlan_HasNoProductOrPrice()
    {
        var result = await _sut.GetPricingAsync();

        result.Should().ContainKey("enterprise");
        result["enterprise"].ProductId.Should().BeNull();
        result["enterprise"].PriceId.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPricingAsync_SecondCall_UsesCache()
    {
        await _sut.GetPricingAsync();
        _productService.Invocations.Clear();
        _priceService.Invocations.Clear();

        var result = await _sut.GetPricingAsync();

        result.Should().ContainKey("pro");
        _productService.Verify(
            s => s.ListAsync(It.IsAny<ProductListOptions>(), null, It.IsAny<CancellationToken>()),
            Times.Never);
        _priceService.Verify(
            s => s.ListAsync(It.IsAny<PriceListOptions>(), null, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SyncAsync_ForceResync()
    {
        await _sut.GetPricingAsync();
        _productService.Invocations.Clear();
        _priceService.Invocations.Clear();

        var result = await _sut.SyncAsync();

        result.Should().ContainKey("pro");
        _productService.Verify(
            s => s.ListAsync(It.IsAny<ProductListOptions>(), null, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // ── Product creation ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPricingAsync_ProductNotFound_CreatesProduct()
    {
        _productService
            .Setup(s => s.ListAsync(It.IsAny<ProductListOptions>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeList<Product> { Data = [] });

        var result = await _sut.GetPricingAsync();

        result["pro"].ProductId.Should().NotBeNull();
        _productService.Verify(
            s => s.CreateAsync(
                It.Is<ProductCreateOptions>(o => o.Name!.Contains("Pro")),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Price creation ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPricingAsync_PriceNotFound_CreatesPrice()
    {
        _priceService
            .Setup(s => s.ListAsync(It.IsAny<PriceListOptions>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeList<Price> { Data = [] });

        var result = await _sut.GetPricingAsync();

        result["pro"].PriceId.Should().NotBeNull();
        _priceService.Verify(
            s => s.CreateAsync(
                It.Is<PriceCreateOptions>(o => o.UnitAmount == 2900 && o.Currency == "usd"),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Error handling ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPricingAsync_StripeExceptionForOnePlan_OthersStillSync()
    {
        // Make ListAsync throw for all paid plans — StripePlanService catches per-plan
        _productService
            .Setup(s => s.ListAsync(It.IsAny<ProductListOptions>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException(System.Net.HttpStatusCode.ServiceUnavailable,
                new StripeError { Message = "API down" }, "Error"));

        var result = await _sut.GetPricingAsync();

        // All plans still present
        result.Should().ContainKey("pro");
        result.Should().ContainKey("free");
        // Failed plans have empty pricing
        result["pro"].ProductId.Should().BeNull();
        result["pro"].PriceId.Should().BeNull();
        result["pro"].PlanId.Should().Be("pro");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPricingAsync_AllPlansInResult()
    {
        var result = await _sut.GetPricingAsync();

        result.Keys.Should().BeEquivalentTo(
            PlanCatalog.All.Select(p => p.Id),
            because: "every plan in the catalog should appear in the pricing map");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private void SetupAllProductsAsNew()
    {
        // Default: no existing products → test the creation path
        _productService
            .Setup(s => s.ListAsync(It.IsAny<ProductListOptions>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeList<Product> { Data = [] });

        _productService
            .Setup(s => s.CreateAsync(It.IsAny<ProductCreateOptions>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductCreateOptions opts, RequestOptions _, CancellationToken _) =>
                new Product
                {
                    Id = $"prod_test_{opts.Metadata["plan_id"]}",
                    Metadata = opts.Metadata
                });
    }

    private void SetupAllPricesAsNew()
    {
        _priceService
            .Setup(s => s.ListAsync(It.IsAny<PriceListOptions>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeList<Price> { Data = [] });

        _priceService
            .Setup(s => s.CreateAsync(It.IsAny<PriceCreateOptions>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PriceCreateOptions opts, RequestOptions _, CancellationToken _) =>
                new Price
                {
                    Id = $"price_test_{opts.Metadata["plan_id"]}",
                    UnitAmount = opts.UnitAmount,
                    Currency = opts.Currency,
                    Recurring = new PriceRecurring { Interval = "month" },
                    Metadata = opts.Metadata
                });
    }
}
