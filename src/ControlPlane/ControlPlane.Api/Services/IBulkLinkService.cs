using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Api.Services;

public interface IBulkLinkService
{
    Task<BulkCreateLinksResponse> ProcessBulkAsync(Guid tenantId, BulkCreateLinksRequest request);
    Guid? EnqueueAsyncJob(Guid tenantId, BulkCreateLinksRequest request);
    BulkCreateLinksResponse? CheckAsyncJob(Guid jobId);
}
