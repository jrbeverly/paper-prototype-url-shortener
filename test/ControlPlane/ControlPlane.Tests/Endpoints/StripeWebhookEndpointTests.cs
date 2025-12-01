using System.Security.Cryptography;
using System.Text;
using ControlPlane.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class StripeWebhookEndpointTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;

    // Minimal valid Stripe event JSON that EventUtility.ConstructEvent can parse.
    private const string _validPayload = """{"id":"evt_test_123","object":"event","api_version":"2024-04-10","type":"customer.created","data":{"object":{"id":"cus_test","object":"customer"}}}""";

    public StripeWebhookEndpointTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    // ── Signature verification ───────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_ValidSignature_Returns200()
    {
        var client = _factory.CreateClient();
        var signature = BuildSignatureHeader(CustomWebApplicationFactory.TestWebhookSecret, _validPayload);

        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent(_validPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", signature);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_MissingSignatureHeader_Returns400()
    {
        var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent(_validPayload, Encoding.UTF8, "application/json")
        };
        // No Stripe-Signature header

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_InvalidSignature_Returns400()
    {
        var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent(_validPayload, Encoding.UTF8, "application/json")
        };
        // Tampered signature
        request.Headers.Add("Stripe-Signature", "t=1234567890,v1=invalidsignaturehex");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_SignedWithWrongSecret_Returns400()
    {
        var client = _factory.CreateClient();
        var signature = BuildSignatureHeader("wrong_secret", _validPayload);

        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent(_validPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", signature);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Subscription events ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_SubscriptionCreated_UpdatesTenantPlanAndStatus()
    {
        const string customerId = "cus_sub_created_001";
        var tenant = await SeedTenantAsync(customerId, plan: "free");

        var payload = BuildSubscriptionEvent("evt_sub_created_001", "customer.subscription.created",
            customerId, "sub_001", status: "active", plan: "starter");

        await SendWebhookAsync(payload);

        var updated = await GetTenantAsync(tenant.Id);
        updated!.Plan.Should().Be("starter");
        updated.Status.Should().Be("active");
        updated.StripeSubscriptionId.Should().Be("sub_001");
        updated.MaxDomains.Should().Be(10);
        updated.MaxLinksPerDomain.Should().Be(1_000);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_SubscriptionUpdated_UpdatesTenantPlanAndStatus()
    {
        const string customerId = "cus_sub_updated_001";
        await SeedTenantAsync(customerId, plan: "starter");

        var payload = BuildSubscriptionEvent("evt_sub_updated_001", "customer.subscription.updated",
            customerId, "sub_002", status: "active", plan: "pro");

        await SendWebhookAsync(payload);

        var updated = await GetTenantByCustomerIdAsync(customerId);
        updated!.Plan.Should().Be("pro");
        updated.Status.Should().Be("active");
        updated.MaxDomains.Should().Be(50);
        updated.MaxLinksPerDomain.Should().Be(10_000);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_SubscriptionUpdated_PastDue_SetsTenantStatusPastDue()
    {
        const string customerId = "cus_sub_pastdue_001";
        await SeedTenantAsync(customerId, plan: "pro");

        var payload = BuildSubscriptionEvent("evt_sub_pastdue_001", "customer.subscription.updated",
            customerId, "sub_003", status: "past_due", plan: "pro");

        await SendWebhookAsync(payload);

        var updated = await GetTenantByCustomerIdAsync(customerId);
        updated!.Status.Should().Be("past_due");
        updated.Plan.Should().Be("pro");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_SubscriptionDeleted_DowngradesToFree()
    {
        const string customerId = "cus_sub_deleted_001";
        await SeedTenantAsync(customerId, plan: "pro", subscriptionId: "sub_004");

        var payload = BuildSubscriptionEvent("evt_sub_deleted_001", "customer.subscription.deleted",
            customerId, "sub_004", status: "canceled", plan: "pro");

        await SendWebhookAsync(payload);

        var updated = await GetTenantByCustomerIdAsync(customerId);
        updated!.Plan.Should().Be("free");
        updated.Status.Should().Be("active");
        updated.StripeSubscriptionId.Should().BeNull();
        updated.MaxDomains.Should().Be(3);
        updated.MaxLinksPerDomain.Should().Be(100);
    }

    // ── Invoice payment events ───────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_PaymentSucceeded_ClearsPastDueStatus()
    {
        const string customerId = "cus_payment_ok_001";
        await SeedTenantAsync(customerId, plan: "pro", status: "past_due",
            paymentFailedAt: DateTime.UtcNow.AddDays(-3));

        var payload = BuildInvoiceEvent("evt_payment_ok_001", "invoice.payment_succeeded",
            customerId, "in_001");

        await SendWebhookAsync(payload);

        var updated = await GetTenantByCustomerIdAsync(customerId);
        updated!.Status.Should().Be("active");
        updated.PaymentFailedAt.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_PaymentFailed_SetsPastDueAndRecordsFirstFailure()
    {
        const string customerId = "cus_payment_fail_001";
        await SeedTenantAsync(customerId, plan: "pro", status: "active");

        var payload = BuildInvoiceEvent("evt_payment_fail_001", "invoice.payment_failed",
            customerId, "in_002");

        var before = DateTime.UtcNow;
        await SendWebhookAsync(payload);
        var after = DateTime.UtcNow;

        var updated = await GetTenantByCustomerIdAsync(customerId);
        updated!.Status.Should().Be("past_due");
        updated.PaymentFailedAt.Should().NotBeNull();
        updated.PaymentFailedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_PaymentFailed_PreservesFirstFailureTimestamp()
    {
        const string customerId = "cus_payment_fail_002";
        var originalFailedAt = DateTime.UtcNow.AddDays(-2);
        await SeedTenantAsync(customerId, plan: "pro", status: "past_due",
            paymentFailedAt: originalFailedAt);

        var payload = BuildInvoiceEvent("evt_payment_fail_002", "invoice.payment_failed",
            customerId, "in_003");

        await SendWebhookAsync(payload);

        var updated = await GetTenantByCustomerIdAsync(customerId);
        // First failure timestamp must not be overwritten on retry failures.
        updated!.PaymentFailedAt.Should().BeCloseTo(originalFailedAt, TimeSpan.FromSeconds(1));
    }

    // ── Checkout session ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_CheckoutSessionCompleted_UpdatesTenantPlanAndSubscription()
    {
        const string customerId = "cus_checkout_001";
        await SeedTenantAsync(customerId, plan: "free");

        var payload = BuildCheckoutSessionEvent("evt_checkout_001", customerId,
            subscriptionId: "sub_checkout_001", plan: "pro");

        await SendWebhookAsync(payload);

        var updated = await GetTenantByCustomerIdAsync(customerId);
        updated!.Plan.Should().Be("pro");
        updated.Status.Should().Be("active");
        updated.StripeSubscriptionId.Should().Be("sub_checkout_001");
        updated.PaymentFailedAt.Should().BeNull();
    }

    // ── Idempotency ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_DuplicateEvent_ProcessedOnlyOnce()
    {
        const string customerId = "cus_idempotent_001";
        await SeedTenantAsync(customerId, plan: "free");

        var payload = BuildSubscriptionEvent("evt_idempotent_001", "customer.subscription.created",
            customerId, "sub_idem_001", status: "active", plan: "starter");

        // Send same event twice
        var response1 = await SendWebhookAsync(payload);
        var response2 = await SendWebhookAsync(payload);

        response1.Should().Be(HttpStatusCode.OK);
        response2.Should().Be(HttpStatusCode.OK);

        // State must reflect a single application of the event (plan is starter, not corrupted).
        var updated = await GetTenantByCustomerIdAsync(customerId);
        updated!.Plan.Should().Be("starter");
        updated.StripeSubscriptionId.Should().Be("sub_idem_001");
    }

    // ── Unknown event type ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_UnknownEventType_Returns200WithoutError()
    {
        // "customer.created" is not a handled event type — should be ignored gracefully.
        var response = await SendWebhookAsync(_validPayload);

        response.Should().Be(HttpStatusCode.OK);
    }

    // ── Unknown customer ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_SubscriptionForUnknownCustomer_Returns200WithoutError()
    {
        // No tenant with this customer ID exists — event must be acknowledged silently.
        var payload = BuildSubscriptionEvent("evt_unknown_cus_001", "customer.subscription.updated",
            "cus_does_not_exist", "sub_xyz", status: "active", plan: "pro");

        var response = await SendWebhookAsync(payload);

        response.Should().Be(HttpStatusCode.OK);
    }

    // ── Upgrade and downgrade flows ─────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_SubscriptionDowngrade_UpdatesPlanAndLimits()
    {
        const string customerId = "cus_downgrade_001";
        await SeedTenantAsync(customerId, plan: "pro", subscriptionId: "sub_downgrade_001");

        var payload = BuildSubscriptionEvent("evt_downgrade_001", "customer.subscription.updated",
            customerId, "sub_downgrade_001", status: "active", plan: "starter");

        await SendWebhookAsync(payload);

        var updated = await GetTenantByCustomerIdAsync(customerId);
        updated!.Plan.Should().Be("starter");
        updated.Status.Should().Be("active");
        updated.MaxDomains.Should().Be(10);
        updated.MaxLinksPerDomain.Should().Be(1_000);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_SubscriptionUpgradeDowngradeSequence_ReflectsEachStep()
    {
        const string customerId = "cus_updown_001";
        await SeedTenantAsync(customerId, plan: "free");

        // Step 1: free → starter
        await SendWebhookAsync(BuildSubscriptionEvent("evt_updown_001", "customer.subscription.created",
            customerId, "sub_updown_001", status: "active", plan: "starter"));
        var afterStarter = await GetTenantByCustomerIdAsync(customerId);
        afterStarter!.Plan.Should().Be("starter");
        afterStarter.MaxDomains.Should().Be(10);

        // Step 2: starter → pro
        await SendWebhookAsync(BuildSubscriptionEvent("evt_updown_002", "customer.subscription.updated",
            customerId, "sub_updown_001", status: "active", plan: "pro"));
        var afterPro = await GetTenantByCustomerIdAsync(customerId);
        afterPro!.Plan.Should().Be("pro");
        afterPro.MaxDomains.Should().Be(50);

        // Step 3: pro → starter (downgrade)
        await SendWebhookAsync(BuildSubscriptionEvent("evt_updown_003", "customer.subscription.updated",
            customerId, "sub_updown_001", status: "active", plan: "starter"));
        var afterDowngrade = await GetTenantByCustomerIdAsync(customerId);
        afterDowngrade!.Plan.Should().Be("starter");
        afterDowngrade.MaxDomains.Should().Be(10);
        afterDowngrade.MaxLinksPerDomain.Should().Be(1_000);
    }

    // ── Full subscription lifecycle ──────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_FullSubscriptionLifecycle_StateTracksEachEvent()
    {
        const string customerId = "cus_lifecycle_001";
        const string subscriptionId = "sub_lifecycle_001";
        await SeedTenantAsync(customerId, plan: "free");

        // 1. Subscription created (starter)
        await SendWebhookAsync(BuildSubscriptionEvent("evt_lc_001", "customer.subscription.created",
            customerId, subscriptionId, status: "active", plan: "starter"));
        var state1 = await GetTenantByCustomerIdAsync(customerId);
        state1!.Plan.Should().Be("starter");
        state1.Status.Should().Be("active");
        state1.StripeSubscriptionId.Should().Be(subscriptionId);

        // 2. Upgrade to pro
        await SendWebhookAsync(BuildSubscriptionEvent("evt_lc_002", "customer.subscription.updated",
            customerId, subscriptionId, status: "active", plan: "pro"));
        var state2 = await GetTenantByCustomerIdAsync(customerId);
        state2!.Plan.Should().Be("pro");
        state2.MaxDomains.Should().Be(50);

        // 3. Payment fails → past_due
        await SendWebhookAsync(BuildInvoiceEvent("evt_lc_003", "invoice.payment_failed",
            customerId, "in_lc_001"));
        var state3 = await GetTenantByCustomerIdAsync(customerId);
        state3!.Status.Should().Be("past_due");
        state3.PaymentFailedAt.Should().NotBeNull();
        state3.Plan.Should().Be("pro");  // Plan unchanged by payment failure

        // 4. Payment recovered
        await SendWebhookAsync(BuildInvoiceEvent("evt_lc_004", "invoice.payment_succeeded",
            customerId, "in_lc_002"));
        var state4 = await GetTenantByCustomerIdAsync(customerId);
        state4!.Status.Should().Be("active");
        state4.PaymentFailedAt.Should().BeNull();

        // 5. Subscription cancelled → downgraded to free
        await SendWebhookAsync(BuildSubscriptionEvent("evt_lc_005", "customer.subscription.deleted",
            customerId, subscriptionId, status: "canceled", plan: "pro"));
        var state5 = await GetTenantByCustomerIdAsync(customerId);
        state5!.Plan.Should().Be("free");
        state5.Status.Should().Be("active");
        state5.StripeSubscriptionId.Should().BeNull();
        state5.MaxDomains.Should().Be(3);
        state5.MaxLinksPerDomain.Should().Be(100);
    }

    // ── Subscription status mapping ──────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_SubscriptionCreated_TrialingStatus_MapsToActive()
    {
        const string customerId = "cus_trialing_001";
        await SeedTenantAsync(customerId, plan: "free");

        var payload = BuildSubscriptionEvent("evt_trialing_001", "customer.subscription.created",
            customerId, "sub_trialing_001", status: "trialing", plan: "starter");

        await SendWebhookAsync(payload);

        var updated = await GetTenantByCustomerIdAsync(customerId);
        updated!.Plan.Should().Be("starter");
        updated.Status.Should().Be("active");  // trialing maps to active
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_SubscriptionUpdated_IncompleteExpiredStatus_MapsToInactive()
    {
        const string customerId = "cus_incomplete_001";
        await SeedTenantAsync(customerId, plan: "starter", subscriptionId: "sub_incomplete_001");

        var payload = BuildSubscriptionEvent("evt_incomplete_001", "customer.subscription.updated",
            customerId, "sub_incomplete_001", status: "incomplete_expired", plan: "starter");

        await SendWebhookAsync(payload);

        var updated = await GetTenantByCustomerIdAsync(customerId);
        updated!.Status.Should().Be("inactive");  // incomplete_expired maps to inactive
        updated.Plan.Should().Be("starter");  // Plan is preserved from event metadata
    }

    // ── Out-of-order events ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_OutOfOrder_SubscriptionUpdatedBeforeCreated_UpdatesTenantState()
    {
        // Tenant has a customerId but subscription.created was never received.
        // subscription.updated arriving first must still update tenant state correctly,
        // because Stripe can deliver events out of order under retry / network conditions.
        const string customerId = "cus_ooo_001";
        await SeedTenantAsync(customerId, plan: "free");

        var payload = BuildSubscriptionEvent("evt_ooo_001", "customer.subscription.updated",
            customerId, "sub_ooo_001", status: "active", plan: "pro");

        var response = await SendWebhookAsync(payload);

        response.Should().Be(HttpStatusCode.OK);
        var updated = await GetTenantByCustomerIdAsync(customerId);
        updated!.Plan.Should().Be("pro");
        updated.Status.Should().Be("active");
        updated.StripeSubscriptionId.Should().Be("sub_ooo_001");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_OutOfOrder_StalePaymentFailedAfterCancellation_HandledWithoutError()
    {
        // subscription.deleted already fired (tenant downgraded to free/active), then a
        // stale invoice.payment_failed arrives (e.g., Stripe retrying an older event).
        // The system must return 200 and not panic; the state reflects the late event.
        const string customerId = "cus_ooo_002";
        await SeedTenantAsync(customerId, plan: "free", status: "active");

        var payload = BuildInvoiceEvent("evt_ooo_002", "invoice.payment_failed",
            customerId, "in_ooo_001");

        var response = await SendWebhookAsync(payload);

        response.Should().Be(HttpStatusCode.OK);
        var updated = await GetTenantByCustomerIdAsync(customerId);
        updated!.Status.Should().Be("past_due");
        updated.PaymentFailedAt.Should().NotBeNull();
    }

    // ── Additional edge cases ────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_SubscriptionDeleted_WhenPastDue_DowngradesToFreeAndClearsPaymentFailure()
    {
        const string customerId = "cus_delete_pastdue_001";
        await SeedTenantAsync(customerId, plan: "pro", status: "past_due",
            subscriptionId: "sub_dpd_001",
            paymentFailedAt: DateTime.UtcNow.AddDays(-5));

        var payload = BuildSubscriptionEvent("evt_delete_pastdue_001", "customer.subscription.deleted",
            customerId, "sub_dpd_001", status: "canceled", plan: "pro");

        await SendWebhookAsync(payload);

        var updated = await GetTenantByCustomerIdAsync(customerId);
        updated!.Plan.Should().Be("free");
        updated.Status.Should().Be("active");
        updated.StripeSubscriptionId.Should().BeNull();
        updated.PaymentFailedAt.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_PaymentFailed_Idempotency_SameEventTwice_ProcessedOnlyOnce()
    {
        const string customerId = "cus_idem_payment_001";
        await SeedTenantAsync(customerId, plan: "pro", status: "active");

        var payload = BuildInvoiceEvent("evt_idem_payment_001", "invoice.payment_failed",
            customerId, "in_idem_001");

        var response1 = await SendWebhookAsync(payload);
        var afterFirst = await GetTenantByCustomerIdAsync(customerId);
        var firstFailedAt = afterFirst!.PaymentFailedAt;

        var response2 = await SendWebhookAsync(payload);

        response1.Should().Be(HttpStatusCode.OK);
        response2.Should().Be(HttpStatusCode.OK);
        var afterSecond = await GetTenantByCustomerIdAsync(customerId);
        afterSecond!.Status.Should().Be("past_due");
        // PaymentFailedAt must be identical — second delivery was a no-op.
        afterSecond.PaymentFailedAt.Should().Be(firstFailedAt);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StripeWebhook_CheckoutSession_NullSubscriptionId_PreservesExistingSubscription()
    {
        const string customerId = "cus_checkout_nosub_001";
        const string existingSubId = "sub_existing_001";
        await SeedTenantAsync(customerId, plan: "starter", subscriptionId: existingSubId);

        // Checkout session without a subscription ID (e.g., one-time payment or upgrade via portal)
        var payload = BuildCheckoutSessionEventWithNullSubscription("evt_checkout_nosub_001",
            customerId, plan: "pro");

        await SendWebhookAsync(payload);

        var updated = await GetTenantByCustomerIdAsync(customerId);
        updated!.Plan.Should().Be("pro");
        // Existing subscription ID must be preserved when session carries no subscription.
        updated.StripeSubscriptionId.Should().Be(existingSubId);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<TenantEntity> SeedTenantAsync(
        string stripeCustomerId,
        string plan = "free",
        string status = "active",
        string? subscriptionId = null,
        DateTime? paymentFailedAt = null)
    {
        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var (maxDomains, maxLinks) = plan switch
        {
            "starter" => (10, 1_000),
            "pro" => (50, 10_000),
            "enterprise" => (1_000, 1_000_000),
            _ => (3, 100)
        };

        var entity = new TenantEntity
        {
            Id = Guid.NewGuid(),
            Name = $"Test Tenant ({stripeCustomerId})",
            Email = $"{stripeCustomerId}@test.example.com",
            Plan = plan,
            Status = status,
            MaxDomains = maxDomains,
            MaxLinksPerDomain = maxLinks,
            CreatedAt = DateTime.UtcNow,
            StripeCustomerId = stripeCustomerId,
            StripeSubscriptionId = subscriptionId,
            PaymentFailedAt = paymentFailedAt
        };

        return await repo.CreateAsync(entity);
    }

    private async Task<TenantEntity?> GetTenantAsync(Guid tenantId)
    {
        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        return await repo.GetByIdAsync(tenantId);
    }

    private async Task<TenantEntity?> GetTenantByCustomerIdAsync(string customerId)
    {
        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        return await repo.GetByStripeCustomerIdAsync(customerId);
    }

    private async Task<HttpStatusCode> SendWebhookAsync(string payload)
    {
        var client = _factory.CreateClient();
        var signature = BuildSignatureHeader(CustomWebApplicationFactory.TestWebhookSecret, payload);
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", signature);
        var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    // Non-interpolated raw string + Replace avoids escaping issues with consecutive JSON braces.
    private static string BuildSubscriptionEvent(
        string eventId, string eventType, string customerId,
        string subscriptionId, string status, string plan) =>
        """{"id":"__eventId__","object":"event","api_version":"2024-04-10","type":"__eventType__","data":{"object":{"id":"__subId__","object":"subscription","customer":"__customerId__","status":"__status__","metadata":{"plan":"__plan__"},"items":{"object":"list","data":[],"has_more":false,"url":"/v1/subscription_items"}}}}"""
        .Replace("__eventId__", eventId)
        .Replace("__eventType__", eventType)
        .Replace("__subId__", subscriptionId)
        .Replace("__customerId__", customerId)
        .Replace("__status__", status)
        .Replace("__plan__", plan);

    private static string BuildInvoiceEvent(
        string eventId, string eventType, string customerId, string invoiceId) =>
        """{"id":"__eventId__","object":"event","api_version":"2024-04-10","type":"__eventType__","data":{"object":{"id":"__invoiceId__","object":"invoice","customer":"__customerId__","status":"open","amount_due":2900,"amount_paid":0,"currency":"usd","created":1700000000}}}"""
        .Replace("__eventId__", eventId)
        .Replace("__eventType__", eventType)
        .Replace("__invoiceId__", invoiceId)
        .Replace("__customerId__", customerId);

    private static string BuildCheckoutSessionEvent(
        string eventId, string customerId, string subscriptionId, string plan) =>
        """{"id":"__eventId__","object":"event","api_version":"2024-04-10","type":"checkout.session.completed","data":{"object":{"id":"cs___eventId__","object":"checkout.session","customer":"__customerId__","subscription":"__subId__","metadata":{"plan":"__plan__"},"status":"complete","mode":"subscription"}}}"""
        .Replace("__eventId__", eventId)
        .Replace("__customerId__", customerId)
        .Replace("__subId__", subscriptionId)
        .Replace("__plan__", plan);

    // Builds a checkout.session.completed payload where subscription is JSON null,
    // simulating a portal or one-time-payment session that carries no subscription ID.
    private static string BuildCheckoutSessionEventWithNullSubscription(
        string eventId, string customerId, string plan) =>
        """{"id":"__eventId__","object":"event","api_version":"2024-04-10","type":"checkout.session.completed","data":{"object":{"id":"cs___eventId__","object":"checkout.session","customer":"__customerId__","subscription":null,"metadata":{"plan":"__plan__"},"status":"complete","mode":"subscription"}}}"""
        .Replace("__eventId__", eventId)
        .Replace("__customerId__", customerId)
        .Replace("__plan__", plan);

    // Computes a Stripe-Signature header value matching how EventUtility.ConstructEvent verifies it:
    // HMAC-SHA256(key=secret, message="{timestamp}.{payload}"), hex-encoded.
    private static string BuildSignatureHeader(string secret, string payload)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{timestamp}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var signature = BitConverter.ToString(hash).Replace("-", "").ToLower();
        return $"t={timestamp},v1={signature}";
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
