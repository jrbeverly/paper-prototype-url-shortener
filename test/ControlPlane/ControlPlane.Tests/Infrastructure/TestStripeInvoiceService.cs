using ControlPlane.Api.Services;

namespace ControlPlane.Tests.Infrastructure;

/// <summary>In-process fake for IInvoiceService. Returns configurable synthetic invoice data.</summary>
public sealed class TestStripeInvoiceService : IInvoiceService
{
    public const string DefaultPdfUrl = "https://pay.stripe.com/invoice/acct_test/test_pdf";

    public static readonly InvoiceLineItemData PlanLineItem = new()
    {
        Id = "il_test_plan",
        Description = "Starter plan",
        Amount = 2900,
        Currency = "usd",
        Type = "plan",
        PeriodStart = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEnd = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    public static readonly InvoiceLineItemData OverageLineItem = new()
    {
        Id = "il_test_overage",
        Description = "Link overage (500 extra links)",
        Amount = 500,
        Currency = "usd",
        Type = "overage",
        PeriodStart = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEnd = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    public static readonly InvoiceLineItemData CreditLineItem = new()
    {
        Id = "il_test_credit",
        Description = "Promotional credit",
        Amount = -500,
        Currency = "usd",
        Type = "credit"
    };

    public static readonly InvoiceData DefaultInvoice = new()
    {
        Id = "in_test_001",
        Status = "paid",
        AmountDue = 2900,
        AmountPaid = 2900,
        Currency = "usd",
        CreatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodStart = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEnd = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        InvoicePdfUrl = DefaultPdfUrl,
        HostedInvoiceUrl = "https://invoice.stripe.com/test_001"
    };

    public static readonly InvoiceDetailData DefaultInvoiceDetail = new()
    {
        Id = "in_test_001",
        Status = "paid",
        AmountDue = 2900,
        AmountPaid = 2900,
        Subtotal = 3400,
        Total = 2900,
        Currency = "usd",
        CreatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodStart = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEnd = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        InvoicePdfUrl = DefaultPdfUrl,
        HostedInvoiceUrl = "https://invoice.stripe.com/test_001",
        LineItems = [PlanLineItem, OverageLineItem, CreditLineItem]
    };

    public static readonly UpcomingInvoiceData DefaultUpcomingInvoice = new()
    {
        AmountDue = 2900,
        Currency = "usd",
        PeriodStart = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEnd = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        NextPaymentAttempt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        LineItems = [PlanLineItem]
    };

    private volatile bool _hasUpcomingInvoice = true;

    /// <summary>Configures whether GetUpcomingAsync returns an invoice or null.</summary>
    public void SetHasUpcomingInvoice(bool value) => _hasUpcomingInvoice = value;

    public Task<InvoiceListData> ListAsync(string stripeCustomerId, string? startingAfter = null, int limit = 10, CancellationToken ct = default)
    {
        var result = new InvoiceListData
        {
            Items = [DefaultInvoice],
            HasMore = false,
            NextCursor = null
        };
        return Task.FromResult(result);
    }

    public Task<InvoiceDetailData?> GetAsync(string invoiceId, string stripeCustomerId, CancellationToken ct = default)
    {
        InvoiceDetailData? result = invoiceId == DefaultInvoice.Id ? DefaultInvoiceDetail : null;
        return Task.FromResult(result);
    }

    public Task<string?> GetPdfUrlAsync(string invoiceId, string stripeCustomerId, CancellationToken ct = default)
    {
        string? result = invoiceId == DefaultInvoice.Id ? DefaultPdfUrl : null;
        return Task.FromResult(result);
    }

    public Task<UpcomingInvoiceData?> GetUpcomingAsync(string stripeCustomerId, CancellationToken ct = default)
    {
        UpcomingInvoiceData? result = _hasUpcomingInvoice ? DefaultUpcomingInvoice : null;
        return Task.FromResult(result);
    }
}
