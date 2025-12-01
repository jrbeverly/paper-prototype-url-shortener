namespace ControlPlane.Api.Models.Responses;

public sealed record InvoiceListResponse
{
    /// <summary>Past invoices, ordered newest first. Excludes drafts.</summary>
    public required List<InvoiceListItemResponse> Items { get; init; }

    /// <summary>Whether more invoices exist beyond this page.</summary>
    public required bool HasMore { get; init; }

    /// <summary>Opaque cursor to pass as startingAfter to retrieve the next page.</summary>
    public string? NextCursor { get; init; }
}

public sealed record InvoiceListItemResponse
{
    /// <summary>Stripe invoice ID (e.g. "in_xxx").</summary>
    public required string Id { get; init; }

    /// <summary>Invoice status: paid, open, overdue, or void.</summary>
    public required string Status { get; init; }

    /// <summary>Amount due in the smallest currency unit (e.g. cents for USD).</summary>
    public required long AmountDue { get; init; }

    /// <summary>Amount already paid.</summary>
    public required long AmountPaid { get; init; }

    /// <summary>ISO 4217 currency code (e.g. "usd").</summary>
    public required string Currency { get; init; }

    public required DateTime CreatedAt { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime? PeriodStart { get; init; }
    public DateTime? PeriodEnd { get; init; }

    /// <summary>Stripe-hosted PDF download URL. Null if no PDF is available.</summary>
    public string? InvoicePdfUrl { get; init; }

    /// <summary>Stripe-hosted invoice page URL.</summary>
    public string? HostedInvoiceUrl { get; init; }

    public string? Description { get; init; }
}

public sealed record InvoiceDetailResponse
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

    /// <summary>Individual charges on this invoice: plan charge, overage charges, credits.</summary>
    public required List<InvoiceLineItemResponse> LineItems { get; init; }
}

public sealed record InvoiceLineItemResponse
{
    public required string Id { get; init; }
    public required string Description { get; init; }

    /// <summary>Amount in smallest currency unit. Negative for credits.</summary>
    public required long Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>Line item type: plan, overage, or credit.</summary>
    public required string Type { get; init; }

    public DateTime? PeriodStart { get; init; }
    public DateTime? PeriodEnd { get; init; }
}

public sealed record UpcomingInvoiceResponse
{
    /// <summary>Estimated amount due for the next billing period.</summary>
    public required long AmountDue { get; init; }

    public required string Currency { get; init; }
    public DateTime? PeriodStart { get; init; }
    public DateTime? PeriodEnd { get; init; }
    public DateTime? NextPaymentAttempt { get; init; }

    /// <summary>Breakdown of estimated charges.</summary>
    public required List<InvoiceLineItemResponse> LineItems { get; init; }
}
