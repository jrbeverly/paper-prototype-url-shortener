using Microsoft.Extensions.Options;
using Stripe;

namespace ControlPlane.Api.Services;

/// <summary>Invoice retrieval backed by the Stripe API.</summary>
public class StripeInvoiceService : IInvoiceService
{
    private readonly InvoiceService _invoiceService;
    private readonly ILogger<StripeInvoiceService> _logger;

    public StripeInvoiceService(IOptions<StripeOptions> options, ILogger<StripeInvoiceService> logger)
        : this(new InvoiceService(new StripeClient(options.Value.SecretKey)), logger) { }

    internal StripeInvoiceService(InvoiceService invoiceService, ILogger<StripeInvoiceService> logger)
    {
        _invoiceService = invoiceService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InvoiceListData> ListAsync(string stripeCustomerId, string? startingAfter = null, int limit = 10, CancellationToken ct = default)
    {
        var options = new InvoiceListOptions
        {
            Customer = stripeCustomerId,
            StartingAfter = startingAfter,
            Limit = limit
        };

        try
        {
            var list = await _invoiceService.ListAsync(options, cancellationToken: ct);
            var items = list.Data
                .Where(i => i.Status != "draft")
                .Select(MapToData)
                .ToList();

            return new InvoiceListData
            {
                Items = items,
                HasMore = list.HasMore,
                NextCursor = list.Data.Count > 0 ? list.Data[^1].Id : null
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to list invoices for customer {CustomerId}: {StripeError}",
                stripeCustomerId, ex.StripeError?.Message ?? ex.Message);
            return new InvoiceListData { Items = [], HasMore = false };
        }
    }

    /// <inheritdoc />
    public async Task<InvoiceDetailData?> GetAsync(string invoiceId, string stripeCustomerId, CancellationToken ct = default)
    {
        var service = _invoiceService;

        try
        {
            var invoice = await service.GetAsync(invoiceId, cancellationToken: ct);
            if (invoice.CustomerId != stripeCustomerId)
                return null;

            return MapToDetailData(invoice);
        }
        catch (StripeException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to get invoice {InvoiceId}: {StripeError}",
                invoiceId, ex.StripeError?.Message ?? ex.Message);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetPdfUrlAsync(string invoiceId, string stripeCustomerId, CancellationToken ct = default)
    {
        var detail = await GetAsync(invoiceId, stripeCustomerId, ct);
        return detail?.InvoicePdfUrl;
    }

    /// <inheritdoc />
    public async Task<UpcomingInvoiceData?> GetUpcomingAsync(string stripeCustomerId, CancellationToken ct = default)
    {
        var service = _invoiceService;

        // Stripe SDK v51 uses CreatePreviewAsync for upcoming invoice previews.
        var options = new InvoiceCreatePreviewOptions { Customer = stripeCustomerId };

        try
        {
            var invoice = await service.CreatePreviewAsync(options, cancellationToken: ct);
            return new UpcomingInvoiceData
            {
                AmountDue = invoice.AmountDue,
                Currency = invoice.Currency,
                PeriodStart = invoice.PeriodStart,
                PeriodEnd = invoice.PeriodEnd,
                NextPaymentAttempt = invoice.NextPaymentAttempt,
                LineItems = invoice.Lines.Data.Select(MapLineItem).ToList()
            };
        }
        catch (StripeException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // No upcoming invoice (customer has no active subscription).
            return null;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to get upcoming invoice for customer {CustomerId}: {StripeError}",
                stripeCustomerId, ex.StripeError?.Message ?? ex.Message);
            return null;
        }
    }

    private static InvoiceData MapToData(Invoice invoice) => new()
    {
        Id = invoice.Id,
        Status = MapStatus(invoice.Status),
        AmountDue = invoice.AmountDue,
        AmountPaid = invoice.AmountPaid,
        Currency = invoice.Currency,
        CreatedAt = invoice.Created,
        DueDate = invoice.DueDate,
        PeriodStart = invoice.PeriodStart,
        PeriodEnd = invoice.PeriodEnd,
        InvoicePdfUrl = invoice.InvoicePdf,
        HostedInvoiceUrl = invoice.HostedInvoiceUrl,
        Description = invoice.Description
    };

    private static InvoiceDetailData MapToDetailData(Invoice invoice) => new()
    {
        Id = invoice.Id,
        Status = MapStatus(invoice.Status),
        AmountDue = invoice.AmountDue,
        AmountPaid = invoice.AmountPaid,
        Subtotal = invoice.Subtotal,
        Total = invoice.Total,
        Currency = invoice.Currency,
        CreatedAt = invoice.Created,
        DueDate = invoice.DueDate,
        PeriodStart = invoice.PeriodStart,
        PeriodEnd = invoice.PeriodEnd,
        InvoicePdfUrl = invoice.InvoicePdf,
        HostedInvoiceUrl = invoice.HostedInvoiceUrl,
        Description = invoice.Description,
        LineItems = invoice.Lines.Data.Select(MapLineItem).ToList()
    };

    private static InvoiceLineItemData MapLineItem(InvoiceLineItem item) => new()
    {
        Id = item.Id,
        Description = item.Description ?? string.Empty,
        Amount = item.Amount,
        Currency = item.Currency,
        Type = MapLineItemType(item),
        PeriodStart = item.Period?.Start,
        PeriodEnd = item.Period?.End
    };

    private static string MapStatus(string? stripeStatus) => stripeStatus switch
    {
        "paid" => "paid",
        "open" => "open",
        "uncollectible" => "overdue",
        "void" => "void",
        _ => "open"
    };

    // In Stripe SDK v51, subscription charges have Parent.Type == "subscription_item".
    // Invoice items (one-offs) use "invoice_item"; negative amounts are credits.
    private static string MapLineItemType(InvoiceLineItem item)
    {
        if (item.Parent?.Type == "subscription_item")
            return "plan";
        return item.Amount < 0 ? "credit" : "overage";
    }
}
