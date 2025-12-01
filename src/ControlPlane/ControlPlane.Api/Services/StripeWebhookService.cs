using Stripe;

namespace ControlPlane.Api.Services;

/// <summary>Handles Stripe billing events by updating the tenant's plan and billing status.</summary>
public sealed class StripeWebhookService : IStripeWebhookService
{
    private readonly ITenantRepository _tenants;
    private readonly IWebhookEventStore _eventStore;
    private readonly ILogger<StripeWebhookService> _logger;

    public StripeWebhookService(
        ITenantRepository tenants,
        IWebhookEventStore eventStore,
        ILogger<StripeWebhookService> logger)
    {
        _tenants = tenants;
        _eventStore = eventStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> HandleAsync(Event stripeEvent, CancellationToken ct = default)
    {
        var isNew = await _eventStore.TryMarkProcessedAsync(stripeEvent.Id, ct);
        if (!isNew)
        {
            _logger.LogInformation(
                "Stripe event {EventType} ({EventId}) already processed, skipping",
                stripeEvent.Type, stripeEvent.Id);
            return false;
        }

        _logger.LogInformation(
            "Processing Stripe event {EventType} ({EventId})",
            stripeEvent.Type, stripeEvent.Id);

        await (stripeEvent.Type switch
        {
            EventTypes.CustomerSubscriptionCreated or EventTypes.CustomerSubscriptionUpdated =>
                HandleSubscriptionChangedAsync(stripeEvent, ct),
            EventTypes.CustomerSubscriptionDeleted =>
                HandleSubscriptionDeletedAsync(stripeEvent, ct),
            EventTypes.InvoicePaymentSucceeded =>
                HandlePaymentSucceededAsync(stripeEvent, ct),
            EventTypes.InvoicePaymentFailed =>
                HandlePaymentFailedAsync(stripeEvent, ct),
            EventTypes.CheckoutSessionCompleted =>
                HandleCheckoutSessionCompletedAsync(stripeEvent, ct),
            _ =>
                LogUnhandledEventAsync(stripeEvent)
        });

        return true;
    }

    private async Task HandleSubscriptionChangedAsync(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Subscription subscription) return;

        var tenant = await _tenants.GetByStripeCustomerIdAsync(subscription.CustomerId);
        if (tenant is null)
        {
            _logger.LogWarning(
                "Stripe event {EventType} ({EventId}): no tenant found for customer {CustomerId}",
                stripeEvent.Type, stripeEvent.Id, subscription.CustomerId);
            return;
        }

        var plan = subscription.Metadata.TryGetValue("plan", out var metaPlan) ? metaPlan : tenant.Plan;
        var status = MapSubscriptionStatus(subscription.Status);
        var (maxDomains, maxLinks) = PlanCatalog.GetLimits(plan);

        await _tenants.UpdateAsync(tenant with
        {
            Plan = plan,
            Status = status,
            MaxDomains = maxDomains,
            MaxLinksPerDomain = maxLinks,
            StripeSubscriptionId = subscription.Id,
            // Preserve PaymentFailedAt only while status remains past_due; clear it on recovery.
            PaymentFailedAt = status is "past_due" ? tenant.PaymentFailedAt : null,
            // Subscription established — no longer on trial.
            TrialPlan = null,
            // Clear any scheduled plan change — the subscription update has taken effect.
            ScheduledPlan = null,
            ScheduledPlanChangeAt = null,
            UpdatedAt = DateTime.UtcNow
        });

        _logger.LogInformation(
            "Tenant {TenantId} subscription {EventType}: plan={Plan}, status={Status}",
            tenant.Id, stripeEvent.Type, plan, status);
    }

    private async Task HandleSubscriptionDeletedAsync(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Subscription subscription) return;

        var tenant = await _tenants.GetByStripeCustomerIdAsync(subscription.CustomerId);
        if (tenant is null)
        {
            _logger.LogWarning(
                "Stripe event {EventType} ({EventId}): no tenant found for customer {CustomerId}",
                stripeEvent.Type, stripeEvent.Id, subscription.CustomerId);
            return;
        }

        var (maxDomains, maxLinks) = PlanCatalog.GetLimits("free");

        await _tenants.UpdateAsync(tenant with
        {
            Plan = "free",
            Status = "active",
            MaxDomains = maxDomains,
            MaxLinksPerDomain = maxLinks,
            StripeSubscriptionId = null,
            PaymentFailedAt = null,
            ScheduledPlan = null,
            ScheduledPlanChangeAt = null,
            UpdatedAt = DateTime.UtcNow
        });

        _logger.LogInformation(
            "Tenant {TenantId} subscription deleted, downgraded to free plan",
            tenant.Id);
    }

    private async Task HandlePaymentSucceededAsync(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Invoice invoice || invoice.CustomerId is null) return;

        var tenant = await _tenants.GetByStripeCustomerIdAsync(invoice.CustomerId);
        if (tenant is null)
        {
            _logger.LogWarning(
                "Stripe event {EventType} ({EventId}): no tenant found for customer {CustomerId}",
                stripeEvent.Type, stripeEvent.Id, invoice.CustomerId);
            return;
        }

        if (tenant.Status != "past_due" && tenant.PaymentFailedAt is null)
        {
            _logger.LogInformation(
                "Tenant {TenantId} payment succeeded for invoice {InvoiceId} (no state change needed)",
                tenant.Id, invoice.Id);
            return;
        }

        await _tenants.UpdateAsync(tenant with
        {
            Status = "active",
            PaymentFailedAt = null,
            UpdatedAt = DateTime.UtcNow
        });

        _logger.LogInformation(
            "Tenant {TenantId} payment succeeded for invoice {InvoiceId}, status restored to active",
            tenant.Id, invoice.Id);
    }

    private async Task HandlePaymentFailedAsync(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Invoice invoice || invoice.CustomerId is null) return;

        var tenant = await _tenants.GetByStripeCustomerIdAsync(invoice.CustomerId);
        if (tenant is null)
        {
            _logger.LogWarning(
                "Stripe event {EventType} ({EventId}): no tenant found for customer {CustomerId}",
                stripeEvent.Type, stripeEvent.Id, invoice.CustomerId);
            return;
        }

        // Preserve the original failure timestamp so grace period is calculated from first failure.
        var failedAt = tenant.PaymentFailedAt ?? DateTime.UtcNow;

        await _tenants.UpdateAsync(tenant with
        {
            Status = "past_due",
            PaymentFailedAt = failedAt,
            UpdatedAt = DateTime.UtcNow
        });

        _logger.LogWarning(
            "Tenant {TenantId} payment failed for invoice {InvoiceId}. " +
            "First failure: {FailedAt:O}. Account will be downgraded to free plan after grace period.",
            tenant.Id, invoice.Id, failedAt);
    }

    private async Task HandleCheckoutSessionCompletedAsync(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Stripe.Checkout.Session session || session.CustomerId is null) return;

        var tenant = await _tenants.GetByStripeCustomerIdAsync(session.CustomerId);
        if (tenant is null)
        {
            _logger.LogWarning(
                "Stripe event {EventType} ({EventId}): no tenant found for customer {CustomerId}",
                stripeEvent.Type, stripeEvent.Id, session.CustomerId);
            return;
        }

        var plan = session.Metadata.TryGetValue("plan", out var metaPlan) ? metaPlan : tenant.Plan;
        var (maxDomains, maxLinks) = PlanCatalog.GetLimits(plan);

        await _tenants.UpdateAsync(tenant with
        {
            Plan = plan,
            Status = "active",
            MaxDomains = maxDomains,
            MaxLinksPerDomain = maxLinks,
            StripeSubscriptionId = session.SubscriptionId ?? tenant.StripeSubscriptionId,
            PaymentFailedAt = null,
            // Checkout completed — trial converted to paid subscription.
            TrialPlan = null,
            // Clear any scheduled downgrade — new paid subscription supersedes it.
            ScheduledPlan = null,
            ScheduledPlanChangeAt = null,
            UpdatedAt = DateTime.UtcNow
        });

        _logger.LogInformation(
            "Tenant {TenantId} checkout completed: plan={Plan}, subscription={SubscriptionId}",
            tenant.Id, plan, session.SubscriptionId);
    }

    private Task LogUnhandledEventAsync(Event stripeEvent)
    {
        _logger.LogDebug(
            "Stripe event {EventType} ({EventId}) received but not handled",
            stripeEvent.Type, stripeEvent.Id);
        return Task.CompletedTask;
    }

    private static string MapSubscriptionStatus(string stripeStatus) => stripeStatus switch
    {
        "active" or "trialing" => "active",
        "past_due" => "past_due",
        "canceled" or "incomplete_expired" => "inactive",
        _ => "active"
    };
}
