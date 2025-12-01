using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Channels;
using Common.ErrorHandling;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using Microsoft.Extensions.Options;
using LinkCfg = ControlPlane.Api.Services.LinkOptions;

namespace ControlPlane.Api.Services;

public sealed class BulkLinkService : IBulkLinkService, IDisposable
{
    private const int _minSlugLength = 6;
    private const int _maxSlugLength = 8;
    private const string _slugAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int _asyncThreshold = 1000;

    private readonly ILinkRepository _linkRepository;
    private readonly IDomainRepository _domainRepository;
    private readonly IOptions<LinkCfg> _options;
    private readonly Channel<AsyncBulkJob> _jobChannel;
    private readonly ConcurrentDictionary<Guid, BulkCreateLinksResponse?> _jobResults;
    private readonly CancellationTokenSource _cts;

    public BulkLinkService(
        ILinkRepository linkRepository,
        IDomainRepository domainRepository,
        IOptions<LinkCfg> options)
    {
        _linkRepository = linkRepository;
        _domainRepository = domainRepository;
        _options = options;
        _jobChannel = Channel.CreateBounded<AsyncBulkJob>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        _jobResults = new ConcurrentDictionary<Guid, BulkCreateLinksResponse?>();
        _cts = new CancellationTokenSource();
        _ = ProcessJobsAsync(_cts.Token);
    }

    public async Task<BulkCreateLinksResponse> ProcessBulkAsync(Guid tenantId, BulkCreateLinksRequest request)
    {
        var links = request.Links;
        var results = new List<BulkLinkResult>(links.Count);
        var currentCount = await _linkRepository.GetCountByTenantAsync(tenantId);
        var limit = _options.Value.MaxLinks;

        var domainHostnames = new Dictionary<Guid, string>();

        for (int i = 0; i < links.Count; i++)
        {
            var item = links[i];
            var validationError = ValidateSingleItem(item);
            if (validationError is not null)
            {
                results.Add(new BulkLinkResult { Index = i, Success = false, Error = validationError });
                continue;
            }

            if (currentCount + results.Count(r => r.Success) >= limit)
            {
                results.Add(new BulkLinkResult
                {
                    Index = i,
                    Success = false,
                    Error = $"Link limit of {limit} exceeded. {results.Count(r => r.Success)} links were created in this batch."
                });
                continue;
            }

            try
            {
                var link = await CreateSingleLinkAsync(tenantId, item, domainHostnames);
                results.Add(new BulkLinkResult { Index = i, Success = true, Link = link });
            }
            catch (DomainException ex)
            {
                results.Add(new BulkLinkResult { Index = i, Success = false, Error = ex.Message });
            }
        }

        return new BulkCreateLinksResponse
        {
            TotalRequested = links.Count,
            Succeeded = results.Count(r => r.Success),
            Failed = results.Count(r => !r.Success),
            Results = results
        };
    }

    public Guid? EnqueueAsyncJob(Guid tenantId, BulkCreateLinksRequest request)
    {
        if (request.Links.Count <= _asyncThreshold)
            return null;

        var jobId = Guid.NewGuid();
        _jobResults[jobId] = null;

        var job = new AsyncBulkJob
        {
            JobId = jobId,
            TenantId = tenantId,
            Request = request
        };

        if (!_jobChannel.Writer.TryWrite(job))
            return null;

        return jobId;
    }

    public BulkCreateLinksResponse? CheckAsyncJob(Guid jobId)
    {
        if (_jobResults.TryGetValue(jobId, out var result))
            return result;
        return null;
    }

    private async Task ProcessJobsAsync(CancellationToken ct)
    {
        await foreach (var job in _jobChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                var result = await ProcessBulkAsync(job.TenantId, job.Request);
                _jobResults[job.JobId] = result;
            }
            catch
            {
                _jobResults[job.JobId] = new BulkCreateLinksResponse
                {
                    TotalRequested = job.Request.Links.Count,
                    Succeeded = 0,
                    Failed = job.Request.Links.Count,
                    Results = job.Request.Links.Select((_, i) => new BulkLinkResult
                    {
                        Index = i,
                        Success = false,
                        Error = "Async job processing failed"
                    }).ToList()
                };
            }
        }
    }

    private static string? ValidateSingleItem(CreateLinkRequest item)
    {
        if (string.IsNullOrWhiteSpace(item.DestinationUrl))
            return "Destination URL is required";

        if (!Uri.TryCreate(item.DestinationUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return "Destination URL must be a valid absolute URL (e.g., https://example.com/page).";

        if (item.Slug is not null)
        {
            if (item.Slug.Length > 100)
                return "Slug cannot exceed 100 characters.";
        }

        var allowedTypes = new HashSet<string> { "301", "302", "307", "308" };
        if (!allowedTypes.Contains(item.RedirectType))
            return $"Redirect type must be one of: 301, 302, 307, 308.";

        if (item.ExpiresAt.HasValue && item.ExpiresAt.Value <= DateTime.UtcNow)
            return "Expires at must be a future date.";

        return null;
    }

    private async Task<CreateLinkResponse> CreateSingleLinkAsync(
        Guid tenantId,
        CreateLinkRequest request,
        Dictionary<Guid, string> domainHostnames)
    {
        if (!domainHostnames.TryGetValue(request.DomainId, out var hostname))
        {
            var domain = await _domainRepository.GetByIdAsync(tenantId, request.DomainId);
            if (domain is null)
                throw new NotFoundException("Domain", request.DomainId.ToString());

            if (domain.Status != "active")
                throw new BadRequestException(
                    $"Domain is in '{domain.Status}' status. Links can only be created on active domains.");

            hostname = domain.Hostname;
            domainHostnames[request.DomainId] = hostname;
        }

        var slug = request.Slug?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(slug))
        {
            slug = await GenerateUniqueSlugAsync(tenantId, request.DomainId);
        }
        else
        {
            var existing = await _linkRepository.GetByDomainAndSlugAsync(tenantId, request.DomainId, slug);
            if (existing is not null)
                throw new ConflictException($"The slug '{slug}' is already in use on this domain.");
        }

        var entity = new LinkEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DomainId = request.DomainId,
            DestinationUrl = request.DestinationUrl.Trim(),
            Slug = slug,
            RedirectType = request.RedirectType,
            ExpiresAt = request.ExpiresAt,
            CreatedAt = DateTime.UtcNow
        };

        await _linkRepository.CreateAsync(entity);

        var shortUrl = $"https://{hostname}/{entity.Slug}";

        return new CreateLinkResponse
        {
            Id = entity.Id,
            DomainId = entity.DomainId,
            DestinationUrl = entity.DestinationUrl,
            Slug = entity.Slug,
            ShortUrl = shortUrl,
            RedirectType = entity.RedirectType,
            ExpiresAt = entity.ExpiresAt,
            CreatedAt = entity.CreatedAt
        };
    }

    private async Task<string> GenerateUniqueSlugAsync(
        Guid tenantId, Guid domainId, int maxAttempts = 10)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var slug = GenerateRandomSlug();
            var existing = await _linkRepository.GetByDomainAndSlugAsync(tenantId, domainId, slug);
            if (existing is null)
                return slug;
        }

        return GenerateRandomSlug(12);
    }

    private static string GenerateRandomSlug(int length = 0)
    {
        if (length <= 0)
            length = RandomNumberGenerator.GetInt32(_minSlugLength, _maxSlugLength + 1);

        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = _slugAlphabet[bytes[i] % _slugAlphabet.Length];
        return new string(chars);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _jobChannel.Writer.TryComplete();
    }

    private struct AsyncBulkJob
    {
        public Guid JobId;
        public Guid TenantId;
        public BulkCreateLinksRequest Request;
    }
}
