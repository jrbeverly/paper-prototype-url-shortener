using ControlPlane.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stripe;

namespace ControlPlane.UnitTests.Services;

/// <summary>
/// Unit tests for StripeInvoiceService: listing, retrieval, upcoming invoices, status mapping, and error handling.
/// </summary>
public sealed class StripeInvoiceServiceTests
{
    private readonly Mock<InvoiceService> _invoiceService;
    private readonly StripeInvoiceService _sut;

    private const string _customerId = "cus_test_123";
    private const string _invoiceId = "in_test_001";

    public StripeInvoiceServiceTests()
    {
        _invoiceService = new Mock<InvoiceService>(new StripeClient("sk_test_dummy"));
        _sut = new StripeInvoiceService(_invoiceService.Object, NullLogger<StripeInvoiceService>.Instance);
    }

    // ── ListAsync ───────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListAsync_ReturnsMappedInvoices()
    {
        var stripeInvoice = CreatePaidInvoice(_invoiceId, 2900);
        var stripeList = new StripeList<Invoice> { Data = [stripeInvoice] };
        _invoiceService
            .Setup(s => s.ListAsync(It.IsAny<InvoiceListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stripeList);

        var result = await _sut.ListAsync(_customerId);

        result.Items.Should().HaveCount(1);
        result.Items[0].Id.Should().Be(_invoiceId);
        result.Items[0].AmountDue.Should().Be(2900);
        result.Items[0].Currency.Should().Be("usd");
        result.Items[0].Status.Should().Be("paid");
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListAsync_FiltersDraftInvoices()
    {
        var paid = CreatePaidInvoice("in_paid", 1000);
        var draft = CreateDraftInvoice("in_draft", 2000);
        _invoiceService
            .Setup(s => s.ListAsync(It.IsAny<InvoiceListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeList<Invoice> { Data = [paid, draft] });

        var result = await _sut.ListAsync(_customerId);

        result.Items.Should().HaveCount(1);
        result.Items[0].Id.Should().Be("in_paid");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListAsync_HasMore_ReturnsCorrectFlag()
    {
        _invoiceService
            .Setup(s => s.ListAsync(It.IsAny<InvoiceListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeList<Invoice> { Data = [CreatePaidInvoice("in_001", 1000)], HasMore = true });

        var result = await _sut.ListAsync(_customerId);

        result.HasMore.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListAsync_SetsNextCursorToLastInvoiceId()
    {
        _invoiceService
            .Setup(s => s.ListAsync(It.IsAny<InvoiceListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeList<Invoice>
            {
                Data = [CreatePaidInvoice("in_first", 1000), CreatePaidInvoice("in_last", 2000)]
            });

        var result = await _sut.ListAsync(_customerId);

        result.NextCursor.Should().Be("in_last");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListAsync_StripeException_ReturnsEmptyList()
    {
        _invoiceService
            .Setup(s => s.ListAsync(It.IsAny<InvoiceListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException(System.Net.HttpStatusCode.InternalServerError,
                new StripeError { Message = "API error" }, "Test error"));

        var result = await _sut.ListAsync(_customerId);

        result.Items.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
    }

    // ── GetAsync ────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_ReturnsDetailWithLineItems()
    {
        var invoice = CreatePaidInvoice(_invoiceId, 2900);
        invoice.Lines = new StripeList<InvoiceLineItem>
        {
            Data =
            [
                new InvoiceLineItem
                {
                    Id = "il_plan",
                    Description = "Pro plan",
                    Amount = 2900,
                    Currency = "usd",
                    Parent = new InvoiceLineItemParent { Type = "subscription_item" }
                }
            ]
        };
        _invoiceService
            .Setup(s => s.GetAsync(_invoiceId, It.IsAny<InvoiceGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);

        var result = await _sut.GetAsync(_invoiceId, _customerId);

        result.Should().NotBeNull();
        result!.Id.Should().Be(_invoiceId);
        result.AmountDue.Should().Be(2900);
        result.LineItems.Should().HaveCount(1);
        result.LineItems[0].Type.Should().Be("plan");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_WrongCustomer_ReturnsNull()
    {
        var invoice = CreatePaidInvoice(_invoiceId, 2900);
        invoice.CustomerId = "cus_other";
        _invoiceService
            .Setup(s => s.GetAsync(_invoiceId, It.IsAny<InvoiceGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);

        var result = await _sut.GetAsync(_invoiceId, _customerId);

        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_NotFound_ReturnsNull()
    {
        _invoiceService
            .Setup(s => s.GetAsync(_invoiceId, It.IsAny<InvoiceGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException(System.Net.HttpStatusCode.NotFound,
                new StripeError { Message = "Not found" }, "Not found"));

        var result = await _sut.GetAsync(_invoiceId, _customerId);

        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_OtherStripeError_ReturnsNull()
    {
        _invoiceService
            .Setup(s => s.GetAsync(_invoiceId, It.IsAny<InvoiceGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException(System.Net.HttpStatusCode.ServiceUnavailable,
                new StripeError { Message = "Service down" }, "Error"));

        var result = await _sut.GetAsync(_invoiceId, _customerId);

        result.Should().BeNull();
    }

    // ── GetPdfUrlAsync ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPdfUrlAsync_ReturnsPdfUrl()
    {
        var invoice = CreatePaidInvoice(_invoiceId, 2900);
        invoice.InvoicePdf = "https://pay.stripe.com/invoice/test_pdf";
        _invoiceService
            .Setup(s => s.GetAsync(_invoiceId, It.IsAny<InvoiceGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);

        var result = await _sut.GetPdfUrlAsync(_invoiceId, _customerId);

        result.Should().Be("https://pay.stripe.com/invoice/test_pdf");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPdfUrlAsync_NoPdfAvailable_ReturnsNull()
    {
        var invoice = CreatePaidInvoice(_invoiceId, 2900);
        invoice.InvoicePdf = null;
        _invoiceService
            .Setup(s => s.GetAsync(_invoiceId, It.IsAny<InvoiceGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);

        var result = await _sut.GetPdfUrlAsync(_invoiceId, _customerId);

        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPdfUrlAsync_WrongCustomer_ReturnsNull()
    {
        var invoice = CreatePaidInvoice(_invoiceId, 2900);
        invoice.CustomerId = "cus_other";
        _invoiceService
            .Setup(s => s.GetAsync(_invoiceId, It.IsAny<InvoiceGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);

        var result = await _sut.GetPdfUrlAsync(_invoiceId, _customerId);

        result.Should().BeNull();
    }

    // ── GetUpcomingAsync ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetUpcomingAsync_ReturnsUpcomingInvoice()
    {
        _invoiceService
            .Setup(s => s.CreatePreviewAsync(It.IsAny<InvoiceCreatePreviewOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Invoice
            {
                AmountDue = 2900,
                Currency = "usd",
                PeriodStart = new DateTime(2026, 6, 1),
                PeriodEnd = new DateTime(2026, 7, 1),
                NextPaymentAttempt = new DateTime(2026, 6, 15),
                Lines = new StripeList<InvoiceLineItem> { Data = [] }
            });

        var result = await _sut.GetUpcomingAsync(_customerId);

        result.Should().NotBeNull();
        result!.AmountDue.Should().Be(2900);
        result.Currency.Should().Be("usd");
        result.PeriodStart.Should().Be(new DateTime(2026, 6, 1));
        result.NextPaymentAttempt.Should().Be(new DateTime(2026, 6, 15));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetUpcomingAsync_NotFound_ReturnsNull()
    {
        _invoiceService
            .Setup(s => s.CreatePreviewAsync(It.IsAny<InvoiceCreatePreviewOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException(System.Net.HttpStatusCode.NotFound,
                new StripeError { Message = "No upcoming invoice" }, "Not found"));

        var result = await _sut.GetUpcomingAsync(_customerId);

        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetUpcomingAsync_StripeError_ReturnsNull()
    {
        _invoiceService
            .Setup(s => s.CreatePreviewAsync(It.IsAny<InvoiceCreatePreviewOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException(System.Net.HttpStatusCode.BadRequest,
                new StripeError { Message = "Invalid customer" }, "Error"));

        var result = await _sut.GetUpcomingAsync(_customerId);

        result.Should().BeNull();
    }

    // ── Status mapping (tested via ListAsync) ───────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("paid", "paid")]
    [InlineData("open", "open")]
    [InlineData("uncollectible", "overdue")]
    [InlineData("void", "void")]
    [InlineData("draft", "open")] // draft is filtered out, but mapping falls to default
    public async Task ListAsync_MapsStatusCorrectly(string stripeStatus, string expectedStatus)
    {
        var invoice = CreatePaidInvoice("in_test", 1000);
        invoice.Status = stripeStatus;
        _invoiceService
            .Setup(s => s.ListAsync(It.IsAny<InvoiceListOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeList<Invoice> { Data = [invoice] });

        var result = await _sut.ListAsync(_customerId);

        if (stripeStatus == "draft")
            result.Items.Should().BeEmpty();
        else
        {
            result.Items.Should().HaveCount(1);
            result.Items[0].Status.Should().Be(expectedStatus);
        }
    }

    // ── Line item type mapping ──────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_LineItemType_MapsSubscriptionItemToPlan()
    {
        var invoice = CreatePaidInvoice(_invoiceId, 2900);
        invoice.Subtotal = 2500;
        invoice.Total = 2900;
        invoice.Lines = new StripeList<InvoiceLineItem>
        {
            Data =
            [
                new InvoiceLineItem
                {
                    Id = "il_sub",
                    Description = "Subscription",
                    Amount = 2500,
                    Currency = "usd",
                    Parent = new InvoiceLineItemParent { Type = "subscription_item" }
                },
                new InvoiceLineItem
                {
                    Id = "il_overage",
                    Description = "Overage charges",
                    Amount = 400,
                    Currency = "usd",
                    Parent = new InvoiceLineItemParent { Type = "invoice_item" }
                },
                new InvoiceLineItem
                {
                    Id = "il_credit",
                    Description = "Credit",
                    Amount = -500,
                    Currency = "usd"
                }
            ]
        };
        _invoiceService
            .Setup(s => s.GetAsync(_invoiceId, It.IsAny<InvoiceGetOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);

        var result = await _sut.GetAsync(_invoiceId, _customerId);

        result!.LineItems.Should().HaveCount(3);
        result.LineItems.Single(li => li.Id == "il_sub").Type.Should().Be("plan");
        result.LineItems.Single(li => li.Id == "il_overage").Type.Should().Be("overage");
        result.LineItems.Single(li => li.Id == "il_credit").Type.Should().Be("credit");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static Invoice CreatePaidInvoice(string id, long amountDue) => new()
    {
        Id = id,
        CustomerId = _customerId,
        Status = "paid",
        AmountDue = amountDue,
        AmountPaid = amountDue,
        Currency = "usd",
        Created = new DateTime(2026, 5, 1),
        PeriodStart = new DateTime(2026, 5, 1),
        PeriodEnd = new DateTime(2026, 6, 1),
        InvoicePdf = $"https://pay.stripe.com/invoice/{id}",
        HostedInvoiceUrl = $"https://invoice.stripe.com/{id}",
        Lines = new StripeList<InvoiceLineItem> { Data = [] }
    };

    private static Invoice CreateDraftInvoice(string id, long amountDue) => new()
    {
        Id = id,
        CustomerId = _customerId,
        Status = "draft",
        AmountDue = amountDue,
        Currency = "usd",
        Created = new DateTime(2026, 5, 1),
        Lines = new StripeList<InvoiceLineItem> { Data = [] }
    };
}
