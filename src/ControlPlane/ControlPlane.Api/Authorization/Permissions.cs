namespace ControlPlane.Api.Authorization;

public static class Permissions
{
    public const string TenantRead = "tenant:read";
    public const string TenantWrite = "tenant:write";
    public const string DomainRead = "domain:read";
    public const string DomainWrite = "domain:write";
    public const string LinkRead = "link:read";
    public const string LinkWrite = "link:write";
    public const string BillingRead = "billing:read";
    public const string BillingWrite = "billing:write";
    public const string ApiKeyManage = "apikey:manage";
    public const string FlagRead = "flag:read";
    public const string FlagWrite = "flag:write";

    /// <summary>Read access to the tenant's immutable audit log. Owner and Admin roles only.</summary>
    public const string AuditRead = "audit:read";

    /// <summary>
    /// Allows bypassing plan limits. Not granted to any standard role — must be explicitly added
    /// to service accounts or support tooling that requires overriding workspace limits.
    /// </summary>
    public const string PlanBypass = "plan:bypass";

    public static readonly IReadOnlySet<string> AllSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        TenantRead, TenantWrite, DomainRead, DomainWrite,
        LinkRead, LinkWrite, BillingRead, BillingWrite, ApiKeyManage,
        FlagRead, FlagWrite, AuditRead
    };

    /// <summary>Viewer: read-only access to tenants, domains, links, and feature flag evaluations.</summary>
    public static readonly IReadOnlySet<string> ViewerSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        TenantRead, DomainRead, LinkRead, FlagRead
    };

    /// <summary>Member: full link management, read-only on domains, tenants, and feature flags.</summary>
    public static readonly IReadOnlySet<string> MemberSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        TenantRead, DomainRead, LinkRead, LinkWrite, FlagRead
    };
}
