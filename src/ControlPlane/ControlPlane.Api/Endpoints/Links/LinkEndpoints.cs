using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Common.ErrorHandling;
using ControlPlane.Api.Extensions;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Endpoints.Links;

/// <summary>Short link CRUD, bulk operations, CSV import/export, and lifecycle management endpoints.</summary>
public class LinkEndpoints : IEndpointGroup

{
    private const int _minSlugLength = 6;
    private const int _maxSlugLength = 8;
    private const string _slugAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/tenants/{tenantId:guid}/links")
            .WithTags("Links");

        group.MapPost("/", CreateLink)
            .WithName("CreateLink")
            .WithOpenApi()
            .WithDescription("Create a new short link. Auto-generates a random slug if none is provided. Checks slug uniqueness within the domain and enforces plan link limits.")
            .RequireAuthorization("link:write")
            .WithValidation<CreateLinkRequest>()
            .Produces<CreateLinkResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status402PaymentRequired)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests);

        group.MapPost("/bulk", BulkCreateLinks)
            .WithName("BulkCreateLinks")
            .WithOpenApi()
            .WithDescription("Create multiple short links in one request. Handles partial failures — each link is processed independently. Use ProcessAsync=true for >1000 links.")
            .RequireAuthorization("link:write")
            .WithValidation<BulkCreateLinksRequest>()
            .Produces<BulkCreateLinksResponse>(StatusCodes.Status200OK)
            .Produces<BulkCreateLinksResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/import", ImportCsv)
            .WithName("ImportLinksCsv")
            .WithOpenApi()
            .WithDescription("Import links from a CSV file. Expected columns: domain, slug, destination, redirect_type. Each row is validated independently.")
            .RequireAuthorization("link:write")
            .Produces<BulkCreateLinksResponse>(StatusCodes.Status200OK)
            .Produces<BulkCreateLinksResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/export", ExportCsv)
            .WithName("ExportLinksCsv")
            .WithOpenApi()
            .WithDescription("Export all links for the tenant as a CSV file. Includes all link fields plus creation dates.")
            .RequireAuthorization("link:read")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/", ListLinks)
            .WithName("ListLinks")
            .WithOpenApi()
            .WithDescription("List links with cursor-based pagination, search, and filtering. Search matches slug prefix and destination URL. Filter by domain and status. Sort by created_at, updated_at, or click_count.")
            .RequireAuthorization("link:read")
            .Produces<LinkListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{linkId:guid}", GetLink)
            .WithName("GetLink")
            .WithOpenApi()
            .WithDescription("Get full link detail including configuration and analytics summary.")
            .RequireAuthorization("link:read")
            .Produces<LinkDetailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{linkId:guid}", DeleteLink)
            .WithName("DeleteLink")
            .WithOpenApi()
            .WithDescription("Soft-delete a link. The slug is released and the link stops serving redirects. Deleted links can be restored within 30 days. Use ?permanent=true to bypass the recovery window and permanently remove the link.")
            .RequireAuthorization("link:write")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{linkId:guid}/restore", RestoreLink)
            .WithName("RestoreLink")
            .WithOpenApi()
            .WithDescription("Restore a soft-deleted link within the 30-day recovery window. The slug must not have been claimed by another link.")
            .RequireAuthorization("link:write")
            .Produces<CreateLinkResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPatch("/{linkId:guid}", UpdateLink)
            .WithName("UpdateLink")
            .WithOpenApi()
            .WithDescription("Partially update an existing link. Only provided fields are changed. Slug and domain are immutable after creation. Each update increments the link version for audit trail and cache invalidation.")
            .RequireAuthorization("link:write")
            .WithValidation<UpdateLinkRequest>()
            .Produces<CreateLinkResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    // ── Single Link Creation ─────────────────────────────────────────────────

    /// <summary>Creates a new short link on a verified domain. Auto-generates a slug if none is provided.</summary>
    private static async Task<IResult> CreateLink(
        Guid tenantId,
        CreateLinkRequest request,
        ILinkRepository linkRepository,
        IDomainRepository domainRepository,
        ITenantRepository tenantRepository,
        IAuditLogService auditLog,
        ILinkRateLimiter rateLimiter,
        IUrlSafetyService urlSafetyService,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ILogger<LinkEndpoints> logger)
    {
        // ── URL Safety Scan ──────────────────────────────────────────────────
        var safetyResult = await urlSafetyService.ScanAsync(request.DestinationUrl);

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "url_safety.scanned",
            resourceType: "url",
            resourceId: request.DestinationUrl,
            actor: user,
            httpContext: httpContext,
            details: $"verdict={safetyResult.Verdict} source={safetyResult.Source} reason={safetyResult.Reason} cached={safetyResult.FromCache}"));

        switch (safetyResult.Verdict)
        {
            case UrlSafetyVerdict.Malicious:
                throw new UrlBlockedException(safetyResult.Reason);
            case UrlSafetyVerdict.Suspicious:
                logger.LogWarning(
                    "URL safety: suspicious destination for tenant {TenantId}: {Url} — {Reason}",
                    tenantId, request.DestinationUrl, safetyResult.Reason);
                break;
            case UrlSafetyVerdict.Safe:
                break;
        }

        var domain = await domainRepository.GetByIdAsync(tenantId, request.DomainId);
        if (domain is null)
            throw new NotFoundException("Domain", request.DomainId.ToString());

        if (domain.Status != "active")
            throw new BadRequestException(
                $"Domain is in '{domain.Status}' status. Links can only be created on active domains.");

        if (!PlanLimitGuard.HasPlanBypass(user))
        {
            var tenant = await tenantRepository.GetByIdAsync(tenantId);

            // ── Rate limiting ────────────────────────────────────────────────────
            var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!rateLimiter.IsExemptIp(clientIp))
            {
                var ipCheck = rateLimiter.CheckIp(clientIp);
                if (!ipCheck.IsAllowed)
                {
                    ApplyRateLimitHeaders(httpContext.Response, ipCheck);
                    throw new TooManyRequestsException(ipCheck.RetryAfterSeconds);
                }

                var plan = PlanCatalog.TryGet(tenant?.Plan ?? "free") ?? PlanCatalog.Get("free");
                var tenantCheck = rateLimiter.CheckTenant(tenantId, plan.LinkCreationPerMinute);
                ApplyRateLimitHeaders(httpContext.Response, tenantCheck);
                if (!tenantCheck.IsAllowed)
                    throw new TooManyRequestsException(tenantCheck.RetryAfterSeconds);
            }

            // ── Plan link-count limit ────────────────────────────────────────────
            var planLimit = tenant?.MaxLinksPerDomain ?? PlanCatalog.GetLimits("free").MaxLinksPerDomain;
            var currentCount = await linkRepository.GetCountByDomainAsync(tenantId, request.DomainId);
            if (currentCount >= PlanLimitGuard.GraceLimit(planLimit))
                throw new LinkLimitExceededException(planLimit);
        }

        var slug = request.Slug?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(slug))
        {
            slug = await GenerateUniqueSlugAsync(tenantId, request.DomainId, linkRepository);
        }
        else
        {
            var existing = await linkRepository.GetByDomainAndSlugAsync(tenantId, request.DomainId, slug);
            if (existing is not null)
                throw new ConflictException(
                    $"The slug '{slug}' is already in use on this domain.");
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
            CreatedAt = DateTime.UtcNow,
            Status = safetyResult.Verdict == UrlSafetyVerdict.Suspicious ? "quarantined" : "active"
        };

        await linkRepository.CreateAsync(entity);

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "link.created",
            resourceType: "link",
            resourceId: entity.Id.ToString(),
            actor: user,
            httpContext: httpContext,
            newValue: AuditLogEntryFactory.Snapshot(new
            {
                slug = entity.Slug,
                domainId = entity.DomainId,
                destinationUrl = entity.DestinationUrl,
                redirectType = entity.RedirectType,
                expiresAt = entity.ExpiresAt
            })));

        var shortUrl = $"https://{domain.Hostname}/{entity.Slug}";

        var response = new CreateLinkResponse
        {
            Id = entity.Id,
            DomainId = entity.DomainId,
            DestinationUrl = entity.DestinationUrl,
            Slug = entity.Slug,
            ShortUrl = shortUrl,
            RedirectType = entity.RedirectType,
            Status = entity.Status,
            MaxClicks = entity.MaxClicks,
            ExpiresAt = entity.ExpiresAt,
            CreatedAt = entity.CreatedAt,
            Version = entity.Version,
            Rules = entity.Rules,
            ExpiryWarning = BuildExpiryWarning(entity),
            SafetyWarning = safetyResult.Verdict == UrlSafetyVerdict.Suspicious
                || safetyResult.Source == "timeout"
                ? new UrlSafetyWarning
                {
                    Verdict = safetyResult.Verdict.ToString(),
                    Reason = safetyResult.Reason,
                    Source = safetyResult.Source
                }
                : null
        };

        return Results.Created($"/tenants/{tenantId}/links/{entity.Id}", response);
    }

    // ── Bulk Link Creation ───────────────────────────────────────────────────

    /// <summary>Creates multiple links in a single request with partial-failure handling.</summary>
    private static async Task<IResult> BulkCreateLinks(
        Guid tenantId,
        BulkCreateLinksRequest request,
        IBulkLinkService bulkLinkService)
    {
        if (request.ProcessAsync || request.Links.Count > 1000)
        {
            var jobId = bulkLinkService.EnqueueAsyncJob(tenantId, request);
            if (jobId is not null)
            {
                return Results.Accepted(
                    $"/tenants/{tenantId}/links/bulk/jobs/{jobId}",
                    new { JobId = jobId, TotalRequested = request.Links.Count, Status = "processing" });
            }
        }

        var result = await bulkLinkService.ProcessBulkAsync(tenantId, request);
        return Results.Ok(result);
    }

    // ── CSV Import ───────────────────────────────────────────────────────────

    /// <summary>Imports links from a multipart/form-data CSV file upload.</summary>
    private static async Task<IResult> ImportCsv(
        Guid tenantId,
        HttpRequest request,
        IDomainRepository domainRepository,
        IBulkLinkService bulkLinkService)
    {
        if (!request.HasFormContentType)
            throw new BadRequestException("Request must be multipart/form-data with a CSV file.");

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new BadRequestException($"Failed to read form data: {ex.Message}");
        }

        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
            throw new BadRequestException("A CSV file named 'file' is required.");

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException("Uploaded file must be a .csv file.");

        var (csvRecords, parseErrors) = CsvParser.ParseLinkCsv(file.OpenReadStream());

        if (csvRecords.Count == 0 && parseErrors.Count > 0)
            throw new BadRequestException($"CSV parsing failed: {string.Join("; ", parseErrors)}");

        var links = new List<CreateLinkRequest>();
        var rowErrors = new List<string>(parseErrors);

        foreach (var record in csvRecords)
        {
            var domain = await domainRepository.GetByHostnameAsync(tenantId, record.Domain);
            if (domain is null)
            {
                rowErrors.Add($"Domain '{record.Domain}' not found");
                continue;
            }

            links.Add(new CreateLinkRequest
            {
                DomainId = domain.Id,
                DestinationUrl = record.Destination,
                Slug = record.Slug,
                RedirectType = record.RedirectType
            });
        }

        var bulkRequest = new BulkCreateLinksRequest
        {
            Links = links,
            ProcessAsync = csvRecords.Count > 1000
        };

        if (bulkRequest.ProcessAsync)
        {
            var jobId = bulkLinkService.EnqueueAsyncJob(tenantId, bulkRequest);
            if (jobId is not null)
            {
                var response = new BulkCreateLinksResponse
                {
                    TotalRequested = csvRecords.Count,
                    Succeeded = 0,
                    Failed = rowErrors.Count,
                    Results = rowErrors.Select((e, i) => new BulkLinkResult
                    {
                        Index = i,
                        Success = false,
                        Error = e
                    }).ToList()
                };

                return Results.Accepted(
                    $"/tenants/{tenantId}/links/bulk/jobs/{jobId}",
                    new { JobId = jobId, TotalRequested = csvRecords.Count, ParseErrors = rowErrors, Status = "processing" });
            }
        }

        var result = await bulkLinkService.ProcessBulkAsync(tenantId, bulkRequest);

        if (rowErrors.Count > 0)
        {
            var allResults = rowErrors.Select((e, i) => new BulkLinkResult
            {
                Index = i,
                Success = false,
                Error = e
            }).Concat(result.Results).ToList();

            return Results.Ok(new BulkCreateLinksResponse
            {
                TotalRequested = result.TotalRequested + rowErrors.Count,
                Succeeded = result.Succeeded,
                Failed = result.Failed + rowErrors.Count,
                Results = allResults
            });
        }

        return Results.Ok(result);
    }

    // ── CSV Export ───────────────────────────────────────────────────────────

    /// <summary>Exports all links for the tenant as a CSV file download.</summary>
    private static async Task<IResult> ExportCsv(
        Guid tenantId,
        ILinkRepository linkRepository,
        IDomainRepository domainRepository)
    {
        var links = await linkRepository.GetAllByTenantAsync(tenantId);

        var sb = new StringBuilder();
        sb.AppendLine("id,domain_id,domain,slug,destination_url,short_url,redirect_type,expires_at,created_at");

        foreach (var link in links)
        {
            var domain = await domainRepository.GetByIdAsync(tenantId, link.DomainId);
            var hostname = domain?.Hostname ?? link.DomainId.ToString();
            var shortUrl = $"https://{hostname}/{link.Slug}";

            sb.Append(CsvParser.EscapeCsvField(link.Id.ToString())).Append(',');
            sb.Append(CsvParser.EscapeCsvField(link.DomainId.ToString())).Append(',');
            sb.Append(CsvParser.EscapeCsvField(hostname)).Append(',');
            sb.Append(CsvParser.EscapeCsvField(link.Slug)).Append(',');
            sb.Append(CsvParser.EscapeCsvField(link.DestinationUrl)).Append(',');
            sb.Append(CsvParser.EscapeCsvField(shortUrl)).Append(',');
            sb.Append(CsvParser.EscapeCsvField(link.RedirectType)).Append(',');
            sb.Append(CsvParser.EscapeCsvField(link.ExpiresAt?.ToString("o") ?? "")).Append(',');
            sb.AppendLine(CsvParser.EscapeCsvField(link.CreatedAt.ToString("o")));
        }

        var csv = sb.ToString();
        var bytes = Encoding.UTF8.GetBytes(csv);

        return Results.File(bytes, "text/csv", $"links-{tenantId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    // ── Link Listing ───────────────────────────────────────────────────────────

    /// <summary>Lists links with cursor-based pagination, search, and filtering.</summary>
    private static async Task<IResult> ListLinks(
        Guid tenantId,
        ILinkRepository linkRepository,
        IDomainRepository domainRepository,
        string? cursor = null,
        int limit = 20,
        string? search = null,
        Guid? domainId = null,
        string? status = null,
        string sort = "created_at")
    {
        if (limit is < 1 or > 100)
            throw new BadRequestException("Limit must be between 1 and 100.");

        var allowedSorts = new HashSet<string> { "created_at", "updated_at", "click_count" };
        if (!allowedSorts.Contains(sort))
            throw new BadRequestException($"Sort must be one of: {string.Join(", ", allowedSorts)}.");

        var query = new LinkListQuery
        {
            Cursor = cursor,
            Limit = limit,
            Search = search,
            DomainId = domainId,
            Status = status,
            Sort = sort
        };

        var result = await linkRepository.ListAsync(tenantId, query);

        var domainCache = new Dictionary<Guid, string>();
        var items = new List<LinkListItemResponse>(result.Items.Count);

        foreach (var link in result.Items)
        {
            if (!domainCache.TryGetValue(link.DomainId, out var hostname))
            {
                var domain = await domainRepository.GetByIdAsync(tenantId, link.DomainId);
                hostname = domain?.Hostname ?? link.DomainId.ToString();
                domainCache[link.DomainId] = hostname;
            }

            items.Add(new LinkListItemResponse
            {
                Id = link.Id,
                DomainId = link.DomainId,
                DomainHostname = hostname,
                DestinationUrl = link.DestinationUrl,
                Slug = link.Slug,
                ShortUrl = $"https://{hostname}/{link.Slug}",
                RedirectType = link.RedirectType,
                Status = link.Status,
                ClickCount = link.ClickCount,
                MaxClicks = link.MaxClicks,
                ExpiresAt = link.ExpiresAt,
                UpdatedAt = link.UpdatedAt,
                CreatedAt = link.CreatedAt,
                Version = link.Version,
                ExpiryWarning = BuildExpiryWarning(link)
            });
        }

        return Results.Ok(new LinkListResponse
        {
            Items = items,
            NextCursor = result.NextCursor,
            TotalCount = result.TotalCount
        });
    }

    // ── Link Detail ────────────────────────────────────────────────────────────

    /// <summary>Gets full link detail including configuration, analytics summary, and audit trail.</summary>
    private static async Task<IResult> GetLink(
        Guid tenantId,
        Guid linkId,
        ILinkRepository linkRepository,
        IDomainRepository domainRepository,
        IAnalyticsService analyticsService)
    {
        var link = await linkRepository.GetByIdAsync(tenantId, linkId);
        if (link is null)
            throw new NotFoundException("Link", linkId.ToString());

        var domain = await domainRepository.GetByIdAsync(tenantId, link.DomainId);
        var hostname = domain?.Hostname ?? link.DomainId.ToString();

        var analytics = await analyticsService.GetLinkAnalyticsAsync(
            tenantId, linkId, link.ClickCount, link.CreatedAt);

        var auditTrail = await analyticsService.GetAuditTrailAsync(
            tenantId, linkId, link.Version, link.CreatedAt, link.UpdatedAt);

        return Results.Ok(new LinkDetailResponse
        {
            Id = link.Id,
            DomainId = link.DomainId,
            DomainHostname = hostname,
            DestinationUrl = link.DestinationUrl,
            Slug = link.Slug,
            ShortUrl = $"https://{hostname}/{link.Slug}",
            RedirectType = link.RedirectType,
            Status = link.Status,
            ClickCount = link.ClickCount,
            MaxClicks = link.MaxClicks,
            ExpiresAt = link.ExpiresAt,
            DeletedAt = link.DeletedAt,
            UpdatedAt = link.UpdatedAt,
            CreatedAt = link.CreatedAt,
            Version = link.Version,
            Rules = link.Rules,
            Analytics = analytics,
            AuditTrail = auditTrail,
            ExpiryWarning = BuildExpiryWarning(link)
        });
    }

    // ── Link Deletion ──────────────────────────────────────────────────────────

    /// <summary>Soft-deletes a link with a 30-day recovery window, or permanently deletes it.</summary>
    private static async Task<IResult> DeleteLink(
        Guid tenantId,
        Guid linkId,
        IAuditLogService auditLog,
        ClaimsPrincipal user,
        HttpContext httpContext,
        bool permanent = false,
        ILinkRepository linkRepository = null!,
        ILogger<LinkEndpoints> logger = null!)
    {
        if (permanent)
        {
            // Read before delete so we can capture the before-state in the audit entry.
            var existing = await linkRepository.GetByIdAsync(tenantId, linkId);

            var removed = await linkRepository.HardDeleteAsync(tenantId, linkId);
            if (!removed)
                throw new NotFoundException("Link", linkId.ToString());

            logger.LogInformation(
                "Link {LinkId} permanently deleted for tenant {TenantId}",
                linkId, tenantId);

            await auditLog.LogAsync(AuditLogEntryFactory.Create(
                tenantId,
                action: "link.hard_deleted",
                resourceType: "link",
                resourceId: linkId.ToString(),
                actor: user,
                httpContext: httpContext,
                oldValue: existing is null ? null : AuditLogEntryFactory.Snapshot(new
                {
                    slug = existing.Slug,
                    destinationUrl = existing.DestinationUrl,
                    status = existing.Status
                })));

            return Results.NoContent();
        }

        var deleted = await linkRepository.SoftDeleteAsync(tenantId, linkId);
        if (deleted is null)
            throw new NotFoundException("Link", linkId.ToString());

        logger.LogInformation(
            "Link {LinkId} soft-deleted for tenant {TenantId}, recoverable until {RecoveryDeadline}",
            linkId, tenantId, deleted.DeletedAt!.Value.AddDays(30));

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "link.deleted",
            resourceType: "link",
            resourceId: linkId.ToString(),
            actor: user,
            httpContext: httpContext,
            oldValue: AuditLogEntryFactory.Snapshot(new { status = "active", slug = deleted.Slug }),
            newValue: AuditLogEntryFactory.Snapshot(new { status = "deleted", deletedAt = deleted.DeletedAt })));

        return Results.NoContent();
    }

    // ── Link Restoration ───────────────────────────────────────────────────────

    /// <summary>Restores a soft-deleted link within the 30-day recovery window.</summary>
    private static async Task<IResult> RestoreLink(
        Guid tenantId,
        Guid linkId,
        ILinkRepository linkRepository,
        IDomainRepository domainRepository,
        IAuditLogService auditLog,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ILogger<LinkEndpoints> logger)
    {
        var restored = await linkRepository.RestoreAsync(tenantId, linkId);
        if (restored is null)
            throw new NotFoundException(
                $"Link with ID '{linkId}' was not found, is not in deleted status, or has exceeded the 30-day recovery window.");

        var domain = await domainRepository.GetByIdAsync(tenantId, restored.DomainId);
        var hostname = domain?.Hostname ?? restored.DomainId.ToString();
        var shortUrl = $"https://{hostname}/{restored.Slug}";

        logger.LogInformation(
            "Link {LinkId} restored for tenant {TenantId}",
            linkId, tenantId);

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "link.restored",
            resourceType: "link",
            resourceId: linkId.ToString(),
            actor: user,
            httpContext: httpContext,
            oldValue: AuditLogEntryFactory.Snapshot(new { status = "deleted" }),
            newValue: AuditLogEntryFactory.Snapshot(new { status = restored.Status, slug = restored.Slug })));

        return Results.Ok(new CreateLinkResponse
        {
            Id = restored.Id,
            DomainId = restored.DomainId,
            DestinationUrl = restored.DestinationUrl,
            Slug = restored.Slug,
            ShortUrl = shortUrl,
            RedirectType = restored.RedirectType,
            Status = restored.Status,
            MaxClicks = restored.MaxClicks,
            ExpiresAt = restored.ExpiresAt,
            DeletedAt = restored.DeletedAt,
            CreatedAt = restored.CreatedAt,
            Version = restored.Version,
            Rules = restored.Rules,
            ExpiryWarning = BuildExpiryWarning(restored)
        });
    }

    // ── Link Update ─────────────────────────────────────────────────────────

    /// <summary>Partially updates an existing link. Only provided fields are changed.</summary>
    private static async Task<IResult> UpdateLink(
        Guid tenantId,
        Guid linkId,
        UpdateLinkRequest request,
        ILinkRepository linkRepository,
        IDomainRepository domainRepository,
        IAuditLogService auditLog,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ILogger<LinkEndpoints> logger)
    {
        var existing = await linkRepository.GetByIdAsync(tenantId, linkId);
        if (existing is null)
            throw new NotFoundException("Link", linkId.ToString());

        if (request.DestinationUrl is not null)
        {
            request = request with { DestinationUrl = request.DestinationUrl.Trim() };
        }

        var updated = await linkRepository.UpdateAsync(tenantId, linkId, request);
        if (updated is null)
            throw new NotFoundException("Link", linkId.ToString());

        var domain = await domainRepository.GetByIdAsync(tenantId, updated.DomainId);
        var hostname = domain?.Hostname ?? updated.DomainId.ToString();

        logger.LogInformation(
            "Link {LinkId} updated for tenant {TenantId}, version {Version}",
            linkId, tenantId, updated.Version);

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "link.updated",
            resourceType: "link",
            resourceId: linkId.ToString(),
            actor: user,
            httpContext: httpContext,
            oldValue: AuditLogEntryFactory.Snapshot(new
            {
                destinationUrl = existing.DestinationUrl,
                redirectType = existing.RedirectType,
                status = existing.Status,
                maxClicks = existing.MaxClicks,
                expiresAt = existing.ExpiresAt
            }),
            newValue: AuditLogEntryFactory.Snapshot(new
            {
                destinationUrl = updated.DestinationUrl,
                redirectType = updated.RedirectType,
                status = updated.Status,
                maxClicks = updated.MaxClicks,
                expiresAt = updated.ExpiresAt
            })));

        return Results.Ok(new CreateLinkResponse
        {
            Id = updated.Id,
            DomainId = updated.DomainId,
            DestinationUrl = updated.DestinationUrl,
            Slug = updated.Slug,
            ShortUrl = $"https://{hostname}/{updated.Slug}",
            RedirectType = updated.RedirectType,
            Status = updated.Status,
            ClickCount = updated.ClickCount,
            MaxClicks = updated.MaxClicks,
            ExpiresAt = updated.ExpiresAt,
            DeletedAt = updated.DeletedAt,
            UpdatedAt = updated.UpdatedAt,
            CreatedAt = updated.CreatedAt,
            Version = updated.Version,
            Rules = updated.Rules,
            ExpiryWarning = BuildExpiryWarning(updated)
        });
    }

    // ── Near-Expiry Warning ───────────────────────────────────────────────────

    private static LinkExpiryWarning? BuildExpiryWarning(LinkEntity link)
    {
        if (link.MaxClicks.HasValue && link.MaxClicks.Value > 0)
        {
            var remaining = link.MaxClicks.Value - link.ClickCount;
            var ratio = 1.0 - (double)remaining / link.MaxClicks.Value;
            if (ratio >= 0.9)
            {
                return new LinkExpiryWarning
                {
                    IsNearingExpiry = true,
                    Type = "clicks",
                    Message = $"This link has used {link.ClickCount} of {link.MaxClicks} clicks ({ratio:P0}). Only {remaining} clicks remain.",
                    RemainingClicks = remaining
                };
            }
        }

        if (link.ExpiresAt.HasValue)
        {
            var remainingDays = (link.ExpiresAt.Value - DateTime.UtcNow).TotalDays;
            if (remainingDays <= 7)
            {
                return new LinkExpiryWarning
                {
                    IsNearingExpiry = true,
                    Type = "time",
                    Message = remainingDays < 0
                        ? "This link has expired."
                        : remainingDays < 1
                            ? $"This link expires in {(int)(remainingDays * 24)} hours."
                            : $"This link expires in {remainingDays:F0} days.",
                    RemainingDays = remainingDays
                };
            }
        }

        return null;
    }

    // ── Slug Generation Helpers ──────────────────────────────────────────────

    private static async Task<string> GenerateUniqueSlugAsync(
        Guid tenantId, Guid domainId, ILinkRepository repository, int maxAttempts = 10)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var slug = GenerateRandomSlug();
            var existing = await repository.GetByDomainAndSlugAsync(tenantId, domainId, slug);
            if (existing is null)
                return slug;
        }

        var fallback = GenerateRandomSlug(12);
        return fallback;
    }

    private static string GenerateRandomSlug(int length = 0)
    {
        if (length <= 0)
            length = RandomNumberGenerator.GetInt32(_minSlugLength, _maxSlugLength + 1);

        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = _slugAlphabet[bytes[i] % _slugAlphabet.Length];
        }
        return new string(chars);
    }

    // ── Rate limit header helpers ────────────────────────────────────────────────

    private static void ApplyRateLimitHeaders(HttpResponse response, RateLimitResult result)
    {
        response.Headers["X-RateLimit-Limit"] = result.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString(System.Globalization.CultureInfo.InvariantCulture);
        response.Headers["X-RateLimit-Reset"] = result.ResetUnixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!result.IsAllowed)
            response.Headers["Retry-After"] = result.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
