using System.Security.Claims;
using ControlPlane.Api.Authorization;

namespace ControlPlane.Api.Services;

/// <summary>Helpers for plan limit enforcement at API endpoints.</summary>
internal static class PlanLimitGuard
{
    private const double _graceFactor = 0.1;

    /// <summary>
    /// Returns the hard-block threshold: the plan limit plus a 10% grace allowance.
    /// The grace allows slight overages during plan transitions without immediate hard blocking.
    /// </summary>
    public static int GraceLimit(int planLimit)
    {
        if (planLimit >= int.MaxValue / 2) return int.MaxValue;
        return (int)Math.Ceiling(planLimit * (1.0 + _graceFactor));
    }

    /// <summary>
    /// Returns true if the caller holds the <c>plan:bypass</c> permission, which allows
    /// limit enforcement to be skipped. Not granted to any standard role.
    /// </summary>
    public static bool HasPlanBypass(ClaimsPrincipal user)
    {
        var permissionsClaim = user.FindFirst("permissions")?.Value;
        if (string.IsNullOrEmpty(permissionsClaim)) return false;
        return permissionsClaim
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(Permissions.PlanBypass, StringComparer.OrdinalIgnoreCase);
    }
}
