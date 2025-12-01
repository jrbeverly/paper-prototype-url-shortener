namespace ControlPlane.Api.Services;

public interface ITenantRepository
{
    Task<TenantEntity> CreateAsync(TenantEntity entity);
    Task<TenantEntity?> GetByIdAsync(Guid tenantId);
    Task<TenantEntity?> GetByEmailAsync(string email);
    Task<TenantEntity?> GetByStripeCustomerIdAsync(string stripeCustomerId);
    Task<TenantEntity> UpdateAsync(TenantEntity entity);

    /// <summary>
    /// Returns all trialing tenants whose <see cref="TenantEntity.TrialEndsAt"/> falls in [<paramref name="from"/>, <paramref name="to"/>).
    /// Used to find trials that have expired (to = now, from = MinValue) or are about to expire (from = now+Nd, to = now+(N+1)d).
    /// </summary>
    Task<IReadOnlyList<TenantEntity>> GetExpiringTrialsAsync(DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>
    /// Returns a paginated list of all tenants, optionally filtered by status.
    /// </summary>
    Task<(IReadOnlyList<TenantEntity> Items, int TotalCount)> ListAsync(
        string? status = null, int page = 1, int pageSize = 20, CancellationToken ct = default);
}

public sealed record TenantEntity
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required string Plan { get; init; }
    public required string Status { get; init; }
    public required int MaxDomains { get; init; }
    public required int MaxLinksPerDomain { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    /// <summary>Stripe customer ID (e.g. "cus_xxx"). Null if Stripe customer creation has not yet succeeded.</summary>
    public string? StripeCustomerId { get; init; }

    /// <summary>Active Stripe subscription ID (e.g. "sub_xxx"). Null if the tenant has no paid subscription.</summary>
    public string? StripeSubscriptionId { get; init; }

    /// <summary>UTC timestamp of the first payment failure in the current delinquency cycle. Cleared on payment success.</summary>
    public DateTime? PaymentFailedAt { get; init; }

    /// <summary>UTC timestamp when the trial period ends. Set on creation; never cleared (serves as audit that a trial was used).</summary>
    public DateTime? TrialEndsAt { get; init; }

    /// <summary>Plan being trialed (e.g. "pro"). Non-null while Status is "trialing"; cleared when converted to a paid subscription.</summary>
    public string? TrialPlan { get; init; }

    /// <summary>UTC timestamp when the tenant was suspended. Null if not currently suspended.</summary>
    public DateTime? SuspendedAt { get; init; }

    /// <summary>Reason provided when the tenant was suspended. Null if not suspended.</summary>
    public string? SuspendedReason { get; init; }

    /// <summary>Plan scheduled to take effect at <see cref="ScheduledPlanChangeAt"/> (e.g. downgrade at period end). Null if no pending change.</summary>
    public string? ScheduledPlan { get; init; }

    /// <summary>UTC timestamp when the scheduled plan change takes effect. Null if no scheduled change.</summary>
    public DateTime? ScheduledPlanChangeAt { get; init; }

    /// <summary>UTC timestamp when the tenant was soft-deleted. Hard delete occurs after the cooling-off period. Null if not deleted.</summary>
    public DateTime? DeletedAt { get; init; }

    /// <summary>Logo URL for workspace branding. Null if not configured.</summary>
    public string? LogoUrl { get; init; }

    /// <summary>Default HTTP redirect type for new links: "301", "302", "307", or "308". Null means use system default (302).</summary>
    public string? DefaultRedirectType { get; init; }

    /// <summary>Whether to receive workspace activity notifications. Defaults to true.</summary>
    public bool NotificationsEnabled { get; init; } = true;

    /// <summary>Email address for workspace notifications. Null means the billing email is used.</summary>
    public string? NotificationEmail { get; init; }
}
