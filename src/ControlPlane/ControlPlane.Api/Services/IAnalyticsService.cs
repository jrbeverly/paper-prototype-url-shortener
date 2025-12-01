using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Api.Services;

public interface IAnalyticsService
{
    Task<LinkAnalyticsSummary> GetLinkAnalyticsAsync(Guid tenantId, Guid linkId, long totalClicks, DateTime createdAt);
    Task<IReadOnlyList<LinkAuditEntry>> GetAuditTrailAsync(Guid tenantId, Guid linkId, int currentVersion, DateTime createdAt, DateTime? updatedAt);
}
