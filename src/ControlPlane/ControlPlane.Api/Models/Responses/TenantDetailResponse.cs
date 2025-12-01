namespace ControlPlane.Api.Models.Responses;

public sealed record TenantDetailResponse
{
    /// <summary>The unique identifier of the tenant workspace.</summary>
    public required Guid Id { get; init; }

    /// <summary>The display name of the tenant workspace.</summary>
    public required string Name { get; init; }

    /// <summary>The billing contact email address.</summary>
    public required string Email { get; init; }

    /// <summary>The active subscription plan: free, starter, pro, team, business, or enterprise.</summary>
    public required string Plan { get; init; }

    /// <summary>The tenant status: active, trialing, past_due, suspended, or deleted.</summary>
    public required string Status { get; init; }

    /// <summary>Plan-based resource limits for this tenant.</summary>
    public required TenantLimits Limits { get; init; }

    /// <summary>Configurable workspace settings.</summary>
    public required TenantSettings Settings { get; init; }

    /// <summary>When the tenant workspace was created.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>When the tenant workspace was last updated.</summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>Trial information. Non-null when the tenant has ever started a free trial.</summary>
    public TrialStatusResponse? Trial { get; init; }
}

public sealed record TenantSettings
{
    /// <summary>Logo URL for workspace branding.</summary>
    public string? LogoUrl { get; init; }

    /// <summary>Default HTTP redirect type for new links: "301", "302", "307", or "308". Null means system default (302).</summary>
    public string? DefaultRedirectType { get; init; }

    /// <summary>Whether to receive workspace activity notifications.</summary>
    public required bool NotificationsEnabled { get; init; }

    /// <summary>Email address for workspace notifications. Null means the billing email is used.</summary>
    public string? NotificationEmail { get; init; }
}

public sealed record TenantListItemResponse
{
    /// <summary>The unique identifier of the tenant workspace.</summary>
    public required Guid Id { get; init; }

    /// <summary>The display name of the tenant workspace.</summary>
    public required string Name { get; init; }

    /// <summary>The billing contact email address.</summary>
    public required string Email { get; init; }

    /// <summary>The active subscription plan.</summary>
    public required string Plan { get; init; }

    /// <summary>The tenant status.</summary>
    public required string Status { get; init; }

    /// <summary>When the tenant workspace was created.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>When the tenant workspace was last updated.</summary>
    public DateTime? UpdatedAt { get; init; }
}

public sealed record TenantListResponse
{
    /// <summary>The list of tenant items for the current page.</summary>
    public required List<TenantListItemResponse> Items { get; init; }

    /// <summary>The current page number (1-based).</summary>
    public required int Page { get; init; }

    /// <summary>The number of items per page.</summary>
    public required int PageSize { get; init; }

    /// <summary>Total number of tenants matching the query (across all pages).</summary>
    public required int TotalCount { get; init; }
}
