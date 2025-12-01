namespace ControlPlane.Api.Services;

public interface IInvoiceService
{
    /// <summary>Lists invoices for a Stripe customer. Excludes drafts.</summary>
    Task<InvoiceListData> ListAsync(string stripeCustomerId, string? startingAfter = null, int limit = 10, CancellationToken ct = default);

    /// <summary>Gets full invoice detail including line items. Returns null if not found or belongs to a different customer.</summary>
    Task<InvoiceDetailData?> GetAsync(string invoiceId, string stripeCustomerId, CancellationToken ct = default);

    /// <summary>Returns the hosted PDF URL for an invoice. Returns null if not found, no PDF available, or belongs to a different customer.</summary>
    Task<string?> GetPdfUrlAsync(string invoiceId, string stripeCustomerId, CancellationToken ct = default);

    /// <summary>Returns the upcoming (estimated next) invoice for a Stripe customer. Returns null if the customer has no upcoming invoice.</summary>
    Task<UpcomingInvoiceData?> GetUpcomingAsync(string stripeCustomerId, CancellationToken ct = default);
}

public sealed record InvoiceData
{
    public required string Id { get; init; }

    /// <summary>Normalised status: paid, open, overdue, void.</summary>
    public required string Status { get; init; }

    public required long AmountDue { get; init; }
    public required long AmountPaid { get; init; }

    /// <summary>ISO 4217 currency code (e.g. "usd").</summary>
    public required string Currency { get; init; }

    public required DateTime CreatedAt { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime? PeriodStart { get; init; }
    public DateTime? PeriodEnd { get; init; }
    public string? InvoicePdfUrl { get; init; }
    public string? HostedInvoiceUrl { get; init; }
    public string? Description { get; init; }
}

public sealed record InvoiceDetailData
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required long AmountDue { get; init; }
    public required long AmountPaid { get; init; }
    public required long Subtotal { get; init; }
    public required long Total { get; init; }
    public required string Currency { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime? PeriodStart { get; init; }
    public DateTime? PeriodEnd { get; init; }
    public string? InvoicePdfUrl { get; init; }
    public string? HostedInvoiceUrl { get; init; }
    public string? Description { get; init; }
    public required List<InvoiceLineItemData> LineItems { get; init; }
}

public sealed record InvoiceListData
{
    public required List<InvoiceData> Items { get; init; }
    public required bool HasMore { get; init; }
    public string? NextCursor { get; init; }
}

public sealed record InvoiceLineItemData
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public required long Amount { get; init; }
    public required string Currency { get; init; }

    /// <summary>Normalised type: plan, overage, or credit.</summary>
    public required string Type { get; init; }

    public DateTime? PeriodStart { get; init; }
    public DateTime? PeriodEnd { get; init; }
}

public sealed record UpcomingInvoiceData
{
    public required long AmountDue { get; init; }
    public required string Currency { get; init; }
    public DateTime? PeriodStart { get; init; }
    public DateTime? PeriodEnd { get; init; }
    public DateTime? NextPaymentAttempt { get; init; }
    public required List<InvoiceLineItemData> LineItems { get; init; }
}
