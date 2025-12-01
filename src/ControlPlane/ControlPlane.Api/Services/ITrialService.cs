using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Api.Services;

public interface ITrialService
{
    /// <summary>Returns the current trial status for <paramref name="tenant"/> without touching the database.</summary>
    TrialStatusResponse GetStatus(TenantEntity tenant);

    /// <summary>
    /// Returns a copy of <paramref name="tenant"/> with trial fields populated.
    /// Has no effect if the tenant has already used a trial (TrialEndsAt is set).
    /// Does NOT persist — caller is responsible for saving the returned entity.
    /// </summary>
    TenantEntity StartTrial(TenantEntity tenant);

    /// <summary>Downgrades <paramref name="tenant"/> to the Free plan and persists the change. No-op if already not trialing.</summary>
    Task<TenantEntity> ExpireAsync(TenantEntity tenant, CancellationToken ct = default);

    /// <summary>
    /// Extends the trial of <paramref name="tenant"/> by <paramref name="additionalDays"/> and persists the change.
    /// Throws <see cref="InvalidOperationException"/> if the tenant is not currently on trial.
    /// </summary>
    Task<TenantEntity> ExtendAsync(TenantEntity tenant, int additionalDays, CancellationToken ct = default);
}
