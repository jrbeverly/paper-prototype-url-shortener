using Common.ErrorHandling;
using ControlPlane.Api.Services;
using Microsoft.Extensions.Options;
using Stripe;

namespace ControlPlane.Api.Endpoints.Webhooks;

/// <summary>Stripe webhook receiver. Verifies event signatures and dispatches billing events.</summary>
public static class StripeWebhookEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/webhooks/stripe", HandleWebhook)
            .WithTags("Webhooks")
            .WithName("HandleStripeWebhook")
            .WithOpenApi()
            .WithDescription("Receives Stripe billing events. Verifies the Stripe-Signature header before processing.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .AllowAnonymous();
    }

    private static async Task<IResult> HandleWebhook(
        HttpRequest request,
        IOptions<StripeOptions> stripeOptions,
        IStripeWebhookService webhookService,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ControlPlane.Api.Endpoints.Webhooks.Stripe");
        string payload;
        try
        {
            using var reader = new StreamReader(request.Body);
            payload = await reader.ReadToEndAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to read Stripe webhook request body");
            throw new BadRequestException("Failed to read webhook request body.");
        }

        var signature = request.Headers["Stripe-Signature"].FirstOrDefault();
        if (string.IsNullOrEmpty(signature))
        {
            logger.LogWarning("Stripe webhook received without Stripe-Signature header");
            throw new BadRequestException("Missing Stripe-Signature header.");
        }

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                payload,
                signature,
                stripeOptions.Value.WebhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe webhook signature verification failed");
            throw new BadRequestException("Webhook signature verification failed.");
        }

        logger.LogInformation(
            "Stripe webhook {EventType} ({EventId}) verified",
            stripeEvent.Type, stripeEvent.Id);

        await webhookService.HandleAsync(stripeEvent, ct);

        return Results.Ok();
    }
}
