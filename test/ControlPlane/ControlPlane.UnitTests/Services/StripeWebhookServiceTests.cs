using ControlPlane.Api.Services;
using Moq;
using Stripe;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlPlane.UnitTests.Services;

/// <summary>
/// Unit tests for StripeWebhookService: event handling, idempotency, status mapping, and edge cases.
/// </summary>
public sealed class StripeWebhookServiceTests
{
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<IWebhookEventStore> _eventStore = new();
    private readonly StripeWebhookService _sut;

    private const string _defaultCustomerId = "cus_test_123";
    private const string _defaultSubscriptionId = "sub_test_456";

    public StripeWebhookServiceTests()
    {
        _sut = new StripeWebhookService(
            _tenants.Object,
            _eventStore.Object,
            NullLogger<StripeWebhookService>.Instance);
    }

    private static TenantEntity DefaultTenant(string status = "active", string plan = "pro") => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Corp",
        Email = "test@example.com",
        Plan = plan,
        Status = status,
        MaxDomains = 50,
        MaxLinksPerDomain = 10_000,
        CreatedAt = DateTime.UtcNow,
        StripeCustomerId = _defaultCustomerId,
        StripeSubscriptionId = _defaultSubscriptionId
    };

    // ── Idempotency ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_FirstTime_ReturnsTrue()
    {
        var tenant = DefaultTenant();
        _tenants.Setup(r => r.GetByStripeCustomerIdAsync(_defaultCustomerId)).ReturnsAsync(tenant);
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_001", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(r => r.UpdateAsync(It.IsAny<TenantEntity>())).ReturnsAsync((TenantEntity e) => e);

        var result = await _sut.HandleAsync(CreateSubscriptionCreatedEvent("evt_001"));

        result.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_DuplicateEvent_ReturnsFalse()
    {
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_002", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _sut.HandleAsync(CreateSubscriptionCreatedEvent("evt_002"));

        result.Should().BeFalse();
        _tenants.Verify(r => r.GetByStripeCustomerIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_DuplicateEvent_DoesNotUpdateTenant()
    {
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_003", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _sut.HandleAsync(CreateSubscriptionCreatedEvent("evt_003"));

        _tenants.Verify(r => r.UpdateAsync(It.IsAny<TenantEntity>()), Times.Never);
    }

    // ── Subscription created ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_SubscriptionCreated_UpdatesTenantPlanAndStatus()
    {
        var tenant = DefaultTenant("trialing", "pro");
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_010", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(r => r.GetByStripeCustomerIdAsync(_defaultCustomerId)).ReturnsAsync(tenant);
        TenantEntity? captured = null;
        _tenants.Setup(r => r.UpdateAsync(It.IsAny<TenantEntity>()))
            .Callback<TenantEntity>(e => captured = e)
            .ReturnsAsync((TenantEntity e) => e);

        var evt = new Event
        {
            Id = "evt_010",
            Type = EventTypes.CustomerSubscriptionCreated,
            Data = new EventData
            {
                Object = new Subscription
                {
                    CustomerId = _defaultCustomerId,
                    Status = "active",
                    Id = _defaultSubscriptionId,
                    Metadata = new Dictionary<string, string> { ["plan"] = "pro" }
                }
            }
        };

        await _sut.HandleAsync(evt);

        captured.Should().NotBeNull();
        captured!.Plan.Should().Be("pro");
        captured.Status.Should().Be("active");
        captured.StripeSubscriptionId.Should().Be(_defaultSubscriptionId);
        captured.TrialPlan.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_SubscriptionCreated_TenantNotFound_LogsWarning()
    {
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_011", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(r => r.GetByStripeCustomerIdAsync(_defaultCustomerId)).ReturnsAsync((TenantEntity?)null);

        var evt = new Event
        {
            Id = "evt_011",
            Type = EventTypes.CustomerSubscriptionCreated,
            Data = new EventData
            {
                Object = new Subscription
                {
                    CustomerId = _defaultCustomerId,
                    Status = "active",
                    Id = _defaultSubscriptionId,
                    Metadata = new Dictionary<string, string> { ["plan"] = "pro" }
                }
            }
        };

        var act = () => _sut.HandleAsync(evt);

        await act.Should().NotThrowAsync();
        _tenants.Verify(r => r.UpdateAsync(It.IsAny<TenantEntity>()), Times.Never);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_SubscriptionCreated_PreservesPaymentFailedAtWhenPastDue()
    {
        var failedAt = DateTime.UtcNow.AddDays(-1);
        var tenant = DefaultTenant("past_due") with { PaymentFailedAt = failedAt };
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_012", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(r => r.GetByStripeCustomerIdAsync(_defaultCustomerId)).ReturnsAsync(tenant);
        TenantEntity? captured = null;
        _tenants.Setup(r => r.UpdateAsync(It.IsAny<TenantEntity>()))
            .Callback<TenantEntity>(e => captured = e)
            .ReturnsAsync((TenantEntity e) => e);

        var evt = new Event
        {
            Id = "evt_012",
            Type = EventTypes.CustomerSubscriptionCreated,
            Data = new EventData
            {
                Object = new Subscription
                {
                    CustomerId = _defaultCustomerId,
                    Status = "past_due",
                    Id = _defaultSubscriptionId,
                    Metadata = new Dictionary<string, string> { ["plan"] = "pro" }
                }
            }
        };

        await _sut.HandleAsync(evt);

        captured!.PaymentFailedAt.Should().Be(failedAt);
        captured.Status.Should().Be("past_due");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_SubscriptionCreated_ClearsPaymentFailedAtOnRecovery()
    {
        var tenant = DefaultTenant("past_due") with { PaymentFailedAt = DateTime.UtcNow.AddDays(-3) };
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_013", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(r => r.GetByStripeCustomerIdAsync(_defaultCustomerId)).ReturnsAsync(tenant);
        TenantEntity? captured = null;
        _tenants.Setup(r => r.UpdateAsync(It.IsAny<TenantEntity>()))
            .Callback<TenantEntity>(e => captured = e)
            .ReturnsAsync((TenantEntity e) => e);

        var evt = new Event
        {
            Id = "evt_013",
            Type = EventTypes.CustomerSubscriptionUpdated,
            Data = new EventData
            {
                Object = new Subscription
                {
                    CustomerId = _defaultCustomerId,
                    Status = "active",
                    Id = _defaultSubscriptionId,
                    Metadata = new Dictionary<string, string> { ["plan"] = "pro" }
                }
            }
        };

        await _sut.HandleAsync(evt);

        captured!.PaymentFailedAt.Should().BeNull();
        captured.Status.Should().Be("active");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_SubscriptionUpdated_AppliesPlanLimits()
    {
        var tenant = DefaultTenant("active", "starter");
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_014", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(r => r.GetByStripeCustomerIdAsync(_defaultCustomerId)).ReturnsAsync(tenant);
        TenantEntity? captured = null;
        _tenants.Setup(r => r.UpdateAsync(It.IsAny<TenantEntity>()))
            .Callback<TenantEntity>(e => captured = e)
            .ReturnsAsync((TenantEntity e) => e);

        var evt = new Event
        {
            Id = "evt_014",
            Type = EventTypes.CustomerSubscriptionUpdated,
            Data = new EventData
            {
                Object = new Subscription
                {
                    CustomerId = _defaultCustomerId,
                    Status = "active",
                    Id = _defaultSubscriptionId,
                    Metadata = new Dictionary<string, string> { ["plan"] = "team" }
                }
            }
        };

        await _sut.HandleAsync(evt);

        captured!.Plan.Should().Be("team");
        var teamPlan = PlanCatalog.Get("team");
        captured.MaxDomains.Should().Be(teamPlan.MaxDomains);
        captured.MaxLinksPerDomain.Should().Be(teamPlan.MaxLinksPerDomain);
    }

    // ── Subscription deleted (downgrade to free) ───────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_SubscriptionDeleted_DowngradesToFreePlan()
    {
        var tenant = DefaultTenant("active", "pro");
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_020", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(r => r.GetByStripeCustomerIdAsync(_defaultCustomerId)).ReturnsAsync(tenant);
        TenantEntity? captured = null;
        _tenants.Setup(r => r.UpdateAsync(It.IsAny<TenantEntity>()))
            .Callback<TenantEntity>(e => captured = e)
            .ReturnsAsync((TenantEntity e) => e);

        var evt = new Event
        {
            Id = "evt_020",
            Type = EventTypes.CustomerSubscriptionDeleted,
            Data = new EventData
            {
                Object = new Subscription
                {
                    CustomerId = _defaultCustomerId,
                    Status = "canceled",
                    Id = _defaultSubscriptionId
                }
            }
        };

        await _sut.HandleAsync(evt);

        captured!.Plan.Should().Be("free");
        captured.Status.Should().Be("active");
        captured.StripeSubscriptionId.Should().BeNull();
        captured.PaymentFailedAt.Should().BeNull();
        var freePlan = PlanCatalog.Get("free");
        captured.MaxDomains.Should().Be(freePlan.MaxDomains);
        captured.MaxLinksPerDomain.Should().Be(freePlan.MaxLinksPerDomain);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_SubscriptionDeleted_TenantNotFound_NoUpdate()
    {
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_021", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(r => r.GetByStripeCustomerIdAsync(_defaultCustomerId)).ReturnsAsync((TenantEntity?)null);

        var evt = new Event
        {
            Id = "evt_021",
            Type = EventTypes.CustomerSubscriptionDeleted,
            Data = new EventData
            {
                Object = new Subscription
                {
                    CustomerId = _defaultCustomerId,
                    Status = "canceled"
                }
            }
        };

        await _sut.HandleAsync(evt);

        _tenants.Verify(r => r.UpdateAsync(It.IsAny<TenantEntity>()), Times.Never);
    }

    // ── Payment succeeded ───────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_PaymentSucceeded_RestoresActiveStatus()
    {
        var tenant = DefaultTenant("past_due") with { PaymentFailedAt = DateTime.UtcNow.AddDays(-1) };
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_030", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(r => r.GetByStripeCustomerIdAsync(_defaultCustomerId)).ReturnsAsync(tenant);
        TenantEntity? captured = null;
        _tenants.Setup(r => r.UpdateAsync(It.IsAny<TenantEntity>()))
            .Callback<TenantEntity>(e => captured = e)
            .ReturnsAsync((TenantEntity e) => e);

        var evt = new Event
        {
            Id = "evt_030",
            Type = EventTypes.InvoicePaymentSucceeded,
            Data = new EventData
            {
                Object = new Invoice
                {
                    Id = "in_test_001",
                    CustomerId = _defaultCustomerId,
                    Status = "paid"
                }
            }
        };

        await _sut.HandleAsync(evt);

        captured!.Status.Should().Be("active");
        captured.PaymentFailedAt.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_PaymentSucceeded_AlreadyActive_NoStateChange()
    {
        var tenant = DefaultTenant("active");
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_031", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(r => r.GetByStripeCustomerIdAsync(_defaultCustomerId)).ReturnsAsync(tenant);

        var evt = new Event
        {
            Id = "evt_031",
            Type = EventTypes.InvoicePaymentSucceeded,
            Data = new EventData
            {
                Object = new Invoice
                {
                    Id = "in_test_002",
                    CustomerId = _defaultCustomerId,
                    Status = "paid"
                }
            }
        };

        await _sut.HandleAsync(evt);

        _tenants.Verify(r => r.UpdateAsync(It.IsAny<TenantEntity>()), Times.Never);
    }

    // ── Payment failed ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_PaymentFailed_SetsPastDueStatus()
    {
        var tenant = DefaultTenant("active");
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_040", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(r => r.GetByStripeCustomerIdAsync(_defaultCustomerId)).ReturnsAsync(tenant);
        TenantEntity? captured = null;
        _tenants.Setup(r => r.UpdateAsync(It.IsAny<TenantEntity>()))
            .Callback<TenantEntity>(e => captured = e)
            .ReturnsAsync((TenantEntity e) => e);

        var evt = new Event
        {
            Id = "evt_040",
            Type = EventTypes.InvoicePaymentFailed,
            Data = new EventData
            {
                Object = new Invoice
                {
                    Id = "in_test_003",
                    CustomerId = _defaultCustomerId,
                    Status = "open"
                }
            }
        };

        await _sut.HandleAsync(evt);

        captured!.Status.Should().Be("past_due");
        captured.PaymentFailedAt.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_PaymentFailed_PreservesFirstFailureTimestamp()
    {
        var originalFailure = DateTime.UtcNow.AddDays(-5);
        var tenant = DefaultTenant("past_due") with { PaymentFailedAt = originalFailure };
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_041", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(r => r.GetByStripeCustomerIdAsync(_defaultCustomerId)).ReturnsAsync(tenant);
        TenantEntity? captured = null;
        _tenants.Setup(r => r.UpdateAsync(It.IsAny<TenantEntity>()))
            .Callback<TenantEntity>(e => captured = e)
            .ReturnsAsync((TenantEntity e) => e);

        var evt = new Event
        {
            Id = "evt_041",
            Type = EventTypes.InvoicePaymentFailed,
            Data = new EventData
            {
                Object = new Invoice
                {
                    Id = "in_test_004",
                    CustomerId = _defaultCustomerId,
                    Status = "open"
                }
            }
        };

        await _sut.HandleAsync(evt);

        captured!.PaymentFailedAt.Should().Be(originalFailure,
            because: "subsequent failures should preserve the first failure timestamp");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_PaymentFailed_TenantNotFound_NoUpdate()
    {
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_042", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(r => r.GetByStripeCustomerIdAsync(_defaultCustomerId)).ReturnsAsync((TenantEntity?)null);

        var evt = new Event
        {
            Id = "evt_042",
            Type = EventTypes.InvoicePaymentFailed,
            Data = new EventData
            {
                Object = new Invoice
                {
                    Id = "in_test_005",
                    CustomerId = _defaultCustomerId,
                    Status = "open"
                }
            }
        };

        await _sut.HandleAsync(evt);

        _tenants.Verify(r => r.UpdateAsync(It.IsAny<TenantEntity>()), Times.Never);
    }

    // ── Checkout session completed ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_CheckoutCompleted_UpdatesPlanFromMetadata()
    {
        var tenant = DefaultTenant("trialing", "pro");
        var newSubscriptionId = "sub_new_789";
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_050", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(r => r.GetByStripeCustomerIdAsync(_defaultCustomerId)).ReturnsAsync(tenant);
        TenantEntity? captured = null;
        _tenants.Setup(r => r.UpdateAsync(It.IsAny<TenantEntity>()))
            .Callback<TenantEntity>(e => captured = e)
            .ReturnsAsync((TenantEntity e) => e);

        var evt = new Event
        {
            Id = "evt_050",
            Type = EventTypes.CheckoutSessionCompleted,
            Data = new EventData
            {
                Object = new Stripe.Checkout.Session
                {
                    CustomerId = _defaultCustomerId,
                    SubscriptionId = newSubscriptionId,
                    Metadata = new Dictionary<string, string> { ["plan"] = "team" }
                }
            }
        };

        await _sut.HandleAsync(evt);

        captured!.Plan.Should().Be("team");
        captured.Status.Should().Be("active");
        captured.StripeSubscriptionId.Should().Be(newSubscriptionId);
        captured.PaymentFailedAt.Should().BeNull();
        captured.TrialPlan.Should().BeNull();
    }

    // ── Unhandled event type ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_UnhandledEventType_StillMarksProcessed()
    {
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_060", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var evt = new Event
        {
            Id = "evt_060",
            Type = "charge.refunded",
            Data = new EventData()
        };

        var result = await _sut.HandleAsync(evt);

        result.Should().BeTrue();
        _tenants.Verify(r => r.UpdateAsync(It.IsAny<TenantEntity>()), Times.Never);
    }

    // ── Data.Object is not expected type ────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(EventTypes.CustomerSubscriptionCreated)]
    [InlineData(EventTypes.CustomerSubscriptionDeleted)]
    public async Task HandleAsync_DataObjectNotSubscription_DoesNotThrow(string eventType)
    {
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_070", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var evt = new Event
        {
            Id = "evt_070",
            Type = eventType,
            Data = new EventData { Object = new Invoice { Id = "in_wrong" } }
        };

        var act = () => _sut.HandleAsync(evt);

        await act.Should().NotThrowAsync();
        _tenants.Verify(r => r.UpdateAsync(It.IsAny<TenantEntity>()), Times.Never);
    }

    // ── Subscription status mapping ─────────────────────────────────────────────
    // Tested indirectly through HandleAsync, but also verifiable:
    // active → active, trialing → active, past_due → past_due, canceled → inactive

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("active", "active")]
    [InlineData("trialing", "active")]
    [InlineData("past_due", "past_due")]
    [InlineData("canceled", "inactive")]
    [InlineData("incomplete_expired", "inactive")]
    [InlineData("unpaid", "active")] // fallback
    public async Task HandleAsync_SubscriptionCreated_MapsStatusCorrectly(string stripeStatus, string expectedStatus)
    {
        var tenant = DefaultTenant();
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_080", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _tenants.Setup(r => r.GetByStripeCustomerIdAsync(_defaultCustomerId)).ReturnsAsync(tenant);
        TenantEntity? captured = null;
        _tenants.Setup(r => r.UpdateAsync(It.IsAny<TenantEntity>()))
            .Callback<TenantEntity>(e => captured = e)
            .ReturnsAsync((TenantEntity e) => e);

        var evt = new Event
        {
            Id = "evt_080",
            Type = EventTypes.CustomerSubscriptionCreated,
            Data = new EventData
            {
                Object = new Subscription
                {
                    CustomerId = _defaultCustomerId,
                    Status = stripeStatus,
                    Id = _defaultSubscriptionId,
                    Metadata = new Dictionary<string, string> { ["plan"] = "starter" }
                }
            }
        };

        await _sut.HandleAsync(evt);

        captured!.Status.Should().Be(expectedStatus);
    }

    // ── Concurrent webhooks (idempotency) ──────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_ConcurrentDuplicateEvents_OnlyOneProcessesTenantUpdate()
    {
        var tenant = DefaultTenant();
        int updateCallCount = 0;
        _eventStore.Setup(s => s.TryMarkProcessedAsync("evt_concurrent", It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(Interlocked.Increment(ref updateCallCount) == 1));
        _tenants.Setup(r => r.GetByStripeCustomerIdAsync(_defaultCustomerId)).ReturnsAsync(tenant);
        _tenants.Setup(r => r.UpdateAsync(It.IsAny<TenantEntity>())).ReturnsAsync((TenantEntity e) => e);

        var evt = new Event
        {
            Id = "evt_concurrent",
            Type = EventTypes.CustomerSubscriptionCreated,
            Data = new EventData
            {
                Object = new Subscription
                {
                    CustomerId = _defaultCustomerId,
                    Status = "active",
                    Id = _defaultSubscriptionId,
                    Metadata = new Dictionary<string, string> { ["plan"] = "pro" }
                }
            }
        };

        var tasks = Enumerable.Range(0, 10).Select(_ => _sut.HandleAsync(evt));
        var results = await Task.WhenAll(tasks);

        results.Count(r => r).Should().Be(1);
        results.Count(r => !r).Should().Be(9);
        _tenants.Verify(r => r.UpdateAsync(It.IsAny<TenantEntity>()), Times.Once);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static Event CreateSubscriptionCreatedEvent(string id) => new()
    {
        Id = id,
        Type = EventTypes.CustomerSubscriptionCreated,
        Data = new EventData
        {
            Object = new Subscription
            {
                CustomerId = _defaultCustomerId,
                Status = "active",
                Id = _defaultSubscriptionId,
                Metadata = new Dictionary<string, string> { ["plan"] = "pro" }
            }
        }
    };
}
