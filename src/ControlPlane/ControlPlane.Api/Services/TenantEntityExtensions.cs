namespace ControlPlane.Api.Services;

/// <summary>Extension methods for <see cref="TenantEntity"/> that derive plan-based properties from <see cref="PlanCatalog"/>.</summary>
public static class TenantEntityExtensions
{
    /// <summary>
    /// Returns <c>true</c> if the tenant's current plan includes the named feature.
    /// Use <see cref="PlanFeatures"/> constants for the <paramref name="feature"/> argument.
    /// </summary>
    public static bool HasFeature(this TenantEntity tenant, string feature) =>
        PlanCatalog.TryGet(tenant.Plan)?.Features.Contains(feature) ?? false;
}
