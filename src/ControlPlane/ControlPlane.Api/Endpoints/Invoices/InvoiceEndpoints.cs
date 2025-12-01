using Common.ErrorHandling;
using ControlPlane.Api.Extensions;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.Api.Endpoints.Invoices;

/// <summary>Billing invoice retrieval endpoints. All data is sourced from Stripe.</summary>
public class InvoiceEndpoints : IEndpointGroup
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/tenants/{tenantId:guid}/invoices")
            .WithTags("Invoices");

        group.MapGet("/", ListInvoices)
            .WithName("ListInvoices")
            .WithOpenApi()
            .WithDescription("List past invoices for the tenant. Excludes drafts. Results are cursor-paginated.")
            .RequireAuthorization("billing:read")
            .Produces<InvoiceListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // "upcoming" is a literal segment — ASP.NET Core resolves it before /{invoiceId}.
        group.MapGet("/upcoming", GetUpcomingInvoice)
            .WithName("GetUpcomingInvoice")
            .WithOpenApi()
            .WithDescription("Returns the estimated next invoice based on the current subscription and usage.")
            .RequireAuthorization("billing:read")
            .Produces<UpcomingInvoiceResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{invoiceId}", GetInvoice)
            .WithName("GetInvoice")
            .WithOpenApi()
            .WithDescription("Get full invoice detail including line items.")
            .RequireAuthorization("billing:read")
            .Produces<InvoiceDetailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{invoiceId}/pdf", GetInvoicePdf)
            .WithName("GetInvoicePdf")
            .WithOpenApi()
            .WithDescription("Redirects to the Stripe-hosted PDF for the invoice.")
            .RequireAuthorization("billing:read")
            .Produces(StatusCodes.Status302Found)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>Lists past invoices with cursor-based pagination.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="tenantRepository">The tenant repository.</param>
    /// <param name="invoiceService">The invoice service.</param>
    /// <param name="startingAfter">Stripe invoice ID to start after (for pagination).</param>
    /// <param name="limit">Items per page (1–100).</param>
    private static async Task<IResult> ListInvoices(
        Guid tenantId,
        ITenantRepository tenantRepository,
        IInvoiceService invoiceService,
        string? startingAfter = null,
        int limit = 10)
    {
        if (limit is < 1 or > 100)
            throw new BadRequestException("Limit must be between 1 and 100.");

        var tenant = await tenantRepository.GetByIdAsync(tenantId);
        if (tenant is null)
            throw new NotFoundException("Tenant", tenantId.ToString());

        if (tenant.StripeCustomerId is null)
            return Results.Ok(new InvoiceListResponse { Items = [], HasMore = false });

        var result = await invoiceService.ListAsync(tenant.StripeCustomerId, startingAfter, limit);

        return Results.Ok(new InvoiceListResponse
        {
            Items = result.Items.Select(MapToListItem).ToList(),
            HasMore = result.HasMore,
            NextCursor = result.NextCursor
        });
    }

    /// <summary>Returns the estimated upcoming invoice for the tenant's current subscription.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="tenantRepository">The tenant repository.</param>
    /// <param name="invoiceService">The invoice service.</param>
    private static async Task<IResult> GetUpcomingInvoice(
        Guid tenantId,
        ITenantRepository tenantRepository,
        IInvoiceService invoiceService)
    {
        var tenant = await tenantRepository.GetByIdAsync(tenantId);
        if (tenant is null)
            throw new NotFoundException("Tenant", tenantId.ToString());

        if (tenant.StripeCustomerId is null)
            throw new NotFoundException("Upcoming invoice", tenantId.ToString());

        var upcoming = await invoiceService.GetUpcomingAsync(tenant.StripeCustomerId);
        if (upcoming is null)
            throw new NotFoundException("Upcoming invoice", tenantId.ToString());

        return Results.Ok(new UpcomingInvoiceResponse
        {
            AmountDue = upcoming.AmountDue,
            Currency = upcoming.Currency,
            PeriodStart = upcoming.PeriodStart,
            PeriodEnd = upcoming.PeriodEnd,
            NextPaymentAttempt = upcoming.NextPaymentAttempt,
            LineItems = upcoming.LineItems.Select(MapLineItem).ToList()
        });
    }

    /// <summary>Gets full invoice detail including line items.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="invoiceId">The Stripe invoice ID.</param>
    /// <param name="tenantRepository">The tenant repository.</param>
    /// <param name="invoiceService">The invoice service.</param>
    private static async Task<IResult> GetInvoice(
        Guid tenantId,
        string invoiceId,
        ITenantRepository tenantRepository,
        IInvoiceService invoiceService)
    {
        var tenant = await tenantRepository.GetByIdAsync(tenantId);
        if (tenant is null)
            throw new NotFoundException("Tenant", tenantId.ToString());

        if (tenant.StripeCustomerId is null)
            throw new NotFoundException("Invoice", invoiceId);

        var invoice = await invoiceService.GetAsync(invoiceId, tenant.StripeCustomerId);
        if (invoice is null)
            throw new NotFoundException("Invoice", invoiceId);

        return Results.Ok(new InvoiceDetailResponse
        {
            Id = invoice.Id,
            Status = invoice.Status,
            AmountDue = invoice.AmountDue,
            AmountPaid = invoice.AmountPaid,
            Subtotal = invoice.Subtotal,
            Total = invoice.Total,
            Currency = invoice.Currency,
            CreatedAt = invoice.CreatedAt,
            DueDate = invoice.DueDate,
            PeriodStart = invoice.PeriodStart,
            PeriodEnd = invoice.PeriodEnd,
            InvoicePdfUrl = invoice.InvoicePdfUrl,
            HostedInvoiceUrl = invoice.HostedInvoiceUrl,
            Description = invoice.Description,
            LineItems = invoice.LineItems.Select(MapLineItem).ToList()
        });
    }

    /// <summary>Redirects to the Stripe-hosted PDF for the invoice.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="invoiceId">The Stripe invoice ID.</param>
    /// <param name="tenantRepository">The tenant repository.</param>
    /// <param name="invoiceService">The invoice service.</param>
    private static async Task<IResult> GetInvoicePdf(
        Guid tenantId,
        string invoiceId,
        ITenantRepository tenantRepository,
        IInvoiceService invoiceService)
    {
        var tenant = await tenantRepository.GetByIdAsync(tenantId);
        if (tenant is null)
            throw new NotFoundException("Tenant", tenantId.ToString());

        if (tenant.StripeCustomerId is null)
            throw new NotFoundException("Invoice", invoiceId);

        var pdfUrl = await invoiceService.GetPdfUrlAsync(invoiceId, tenant.StripeCustomerId);
        if (pdfUrl is null)
            throw new NotFoundException("Invoice PDF", invoiceId);

        return Results.Redirect(pdfUrl);
    }

    private static InvoiceListItemResponse MapToListItem(InvoiceData i) => new()
    {
        Id = i.Id,
        Status = i.Status,
        AmountDue = i.AmountDue,
        AmountPaid = i.AmountPaid,
        Currency = i.Currency,
        CreatedAt = i.CreatedAt,
        DueDate = i.DueDate,
        PeriodStart = i.PeriodStart,
        PeriodEnd = i.PeriodEnd,
        InvoicePdfUrl = i.InvoicePdfUrl,
        HostedInvoiceUrl = i.HostedInvoiceUrl,
        Description = i.Description
    };

    private static InvoiceLineItemResponse MapLineItem(InvoiceLineItemData l) => new()
    {
        Id = l.Id,
        Description = l.Description,
        Amount = l.Amount,
        Currency = l.Currency,
        Type = l.Type,
        PeriodStart = l.PeriodStart,
        PeriodEnd = l.PeriodEnd
    };
}
