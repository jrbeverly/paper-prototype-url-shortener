using System.Security.Claims;
using System.Security.Cryptography;
using Common.ErrorHandling;
using ControlPlane.Api.Extensions;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Endpoints.Domains;

/// <summary>Custom domain registration, management, DNS verification, and ACM certificate provisioning endpoints.</summary>
public class DomainEndpoints : IEndpointGroup
{
    private static readonly TimeSpan _verifyCooldown = TimeSpan.FromSeconds(60);

    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/tenants/{tenantId:guid}/domains")
            .WithTags("Domains");

        group.MapPost("/", CreateDomain)
            .WithName("CreateDomain")
            .WithOpenApi()
            .WithDescription("Register a new custom domain. The domain will be in pending_verification status until DNS records are confirmed.")
            .RequireAuthorization("domain:write")
            .WithValidation<CreateDomainRequest>()
            .Produces<CreateDomainResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status402PaymentRequired)
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/", ListDomains)
            .WithName("ListDomains")
            .WithOpenApi()
            .WithDescription("List all domains for a tenant. Supports pagination and filtering by status.")
            .RequireAuthorization("domain:read")
            .Produces<ListDomainsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{domainId:guid}", GetDomain)
            .WithName("GetDomain")
            .WithOpenApi()
            .WithDescription("Get detailed information about a domain including verification status, certificate status, and link count.")
            .RequireAuthorization("domain:read")
            .Produces<DomainDetailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPatch("/{domainId:guid}", UpdateDomain)
            .WithName("UpdateDomain")
            .WithOpenApi()
            .WithDescription("Update domain settings: default redirect URL, error page branding, and 404 behavior. Only provided fields are updated.")
            .RequireAuthorization("domain:write")
            .WithValidation<UpdateDomainRequest>()
            .Produces<DomainDetailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{domainId:guid}", DeleteDomain)
            .WithName("DeleteDomain")
            .WithOpenApi()
            .WithDescription("Soft-delete a domain. Links associated with this domain will be disabled, the ACM certificate will be removed, and the hostname is held for a grace period before release.")
            .RequireAuthorization("domain:write")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{domainId:guid}/verify", VerifyDomain)
            .WithName("VerifyDomain")
            .WithOpenApi()
            .WithDescription("Trigger DNS verification for a domain. Checks TXT and CNAME records. Auto-provisions an ACM certificate on success. Rate limited to 1 attempt per 60 seconds.")
            .RequireAuthorization("domain:write")
            .Produces<VerifyDomainResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests);

        group.MapPost("/{domainId:guid}/certificate", RequestCertificate)
            .WithName("RequestDomainCertificate")
            .WithOpenApi()
            .WithDescription("Request an ACM TLS certificate for the domain using DNS validation. Returns the DNS CNAME records that must be created for ACM to validate domain ownership.")
            .RequireAuthorization("domain:write")
            .Produces<CertificateProvisionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/{domainId:guid}/certificate", GetCertificateStatus)
            .WithName("GetDomainCertificateStatus")
            .WithOpenApi()
            .WithDescription("Get the current status of the ACM certificate for this domain, including renewal eligibility.")
            .RequireAuthorization("domain:read")
            .Produces<CertificateStatusResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{domainId:guid}/status", GetStatus)
            .WithName("GetDomainStatus")
            .WithOpenApi()
            .WithDescription("Get a lightweight dashboard status snapshot for a domain: health score (green/yellow/red), verification state, DNS record expectations, certificate summary, CloudFront routing status, recent verification attempts, and suggested remediation actions. Derived entirely from stored state — no live DNS or HTTP probes — so it is safe to poll frequently.")
            .RequireAuthorization("domain:read")
            .Produces<DomainStatusResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{domainId:guid}/diagnostics", GetDiagnostics)
            .WithName("GetDomainDiagnostics")
            .WithOpenApi()
            .WithDescription("Run diagnostic checks for a domain: DNS records, TLS certificate, CloudFront distribution, end-to-end HTTPS connectivity, and multi-resolver DNS propagation. Returns structured pass/fail results with detected issues and remediation guidance. Safe to call at any time; all checks are read-only.")
            .RequireAuthorization("domain:read")
            .Produces<DomainDiagnosticsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{domainId:guid}/deactivate", DeactivateDomain)
            .WithName("DeactivateDomain")
            .WithOpenApi()
            .WithDescription("Deactivate a domain: marks all its links inactive, revokes the ACM certificate, and removes the CloudFront distribution tenant. The domain and its data are retained for 30 days so it can be reactivated.")
            .RequireAuthorization("domain:write")
            .Produces<DomainDetailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/{domainId:guid}/reactivate", ReactivateDomain)
            .WithName("ReactivateDomain")
            .WithOpenApi()
            .WithDescription("Reactivate a deactivated domain within the 30-day grace period. Restores links to active, re-provisions the ACM certificate, and recreates the CloudFront distribution tenant.")
            .RequireAuthorization("domain:write")
            .Produces<DomainDetailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    /// <summary>Registers a new custom domain for the tenant.</summary>
    private static async Task<IResult> CreateDomain(
        Guid tenantId,
        CreateDomainRequest request,
        IDomainRepository repository,
        ITenantRepository tenantRepository,
        IOptions<DomainOptions> options,
        IAuditLogService auditLog,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var hostname = request.Hostname.Trim().ToLowerInvariant();

        if (!PlanLimitGuard.HasPlanBypass(user))
        {
            var tenant = await tenantRepository.GetByIdAsync(tenantId);
            var planLimit = tenant?.MaxDomains ?? PlanCatalog.GetLimits("free").MaxDomains;
            var currentCount = await repository.GetCountByTenantAsync(tenantId);
            if (currentCount >= PlanLimitGuard.GraceLimit(planLimit))
                throw new DomainLimitExceededException(planLimit);
        }

        var existing = await repository.GetByHostnameAsync(tenantId, hostname);
        if (existing is not null)
            throw new ConflictException($"A domain with hostname '{hostname}' is already registered.");

        var verificationCode = GenerateVerificationCode();
        var cnameTarget = options.Value.CnameTarget;

        var entity = new DomainEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Hostname = hostname,
            Status = "pending_verification",
            VerificationCode = verificationCode,
            CnameTarget = cnameTarget,
            CreatedAt = DateTime.UtcNow
        };

        await repository.CreateAsync(entity);

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "domain.created",
            resourceType: "domain",
            resourceId: entity.Id.ToString(),
            actor: user,
            httpContext: httpContext,
            newValue: AuditLogEntryFactory.Snapshot(new
            {
                hostname = entity.Hostname,
                status = entity.Status
            })));

        var response = new CreateDomainResponse
        {
            Id = entity.Id,
            Hostname = entity.Hostname,
            Status = entity.Status,
            VerificationInstructions = new DnsVerificationInstructions
            {
                TxtName = hostname,
                TxtValue = $"short-io-verify={verificationCode}",
                CnameName = hostname,
                CnameValue = cnameTarget
            },
            CreatedAt = entity.CreatedAt
        };

        return Results.Created($"/tenants/{tenantId}/domains/{entity.Id}", response);
    }

    /// <summary>Lists all domains for a tenant with pagination and optional status filter.</summary>
    private static async Task<IResult> ListDomains(
        Guid tenantId,
        IDomainRepository repository,
        string? status = null,
        int page = 1,
        int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var (items, totalCount) = await repository.ListAsync(tenantId, status, page, pageSize);

        var response = new ListDomainsResponse
        {
            Items = items.Select(e => new DomainListItemResponse
            {
                Id = e.Id,
                Hostname = e.Hostname,
                Status = e.Status,
                CertificateStatus = e.CertificateStatus,
                CertificateArn = e.CertificateArn,
                LinkCount = e.LinkCount,
                CreatedAt = e.CreatedAt
            }).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Results.Ok(response);
    }

    /// <summary>Gets detailed information about a domain, including verification and certificate status.</summary>
    private static async Task<IResult> GetDomain(
        Guid tenantId,
        Guid domainId,
        IDomainRepository repository)
    {
        var entity = await repository.GetByIdAsync(tenantId, domainId);
        if (entity is null)
            throw new NotFoundException("Domain", domainId.ToString());

        var response = MapToDetailResponse(entity);
        return Results.Ok(response);
    }

    /// <summary>Updates domain settings: default redirect URL, error page branding, and 404 behavior.</summary>
    private static async Task<IResult> UpdateDomain(
        Guid tenantId,
        Guid domainId,
        UpdateDomainRequest request,
        IDomainRepository repository,
        IDistributionService distributionService,
        IAuditLogService auditLog,
        ILogger<DomainEndpoints> logger,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var entity = await repository.GetByIdAsync(tenantId, domainId);
        if (entity is null)
            throw new NotFoundException("Domain", domainId.ToString());

        var updated = entity with
        {
            DefaultRedirectUrl = request.DefaultRedirectUrl ?? entity.DefaultRedirectUrl,
            ErrorPageBranding = request.ErrorPageBranding ?? entity.ErrorPageBranding,
            NotFoundBehavior = request.NotFoundBehavior ?? entity.NotFoundBehavior,
            UpdatedAt = DateTime.UtcNow
        };

        await repository.UpdateAsync(updated);

        // Propagate configuration changes to the CloudFront distribution tenant.
        if (!string.IsNullOrEmpty(entity.DistributionTenantId) &&
            !string.IsNullOrEmpty(entity.DistributionTenantETag))
        {
            try
            {
                var result = await distributionService.UpdateDistributionTenantAsync(
                    entity.DistributionTenantId,
                    entity.DistributionTenantETag,
                    entity.Hostname,
                    entity.CertificateArn);

                updated = updated with
                {
                    DistributionTenantStatus = result.Status,
                    DistributionTenantETag = result.ETag
                };
                await repository.UpdateAsync(updated);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Distribution tenant update failed for domain {DomainId} {Hostname}: {Message}",
                    domainId, entity.Hostname, ex.Message);
            }
        }

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "domain.updated",
            resourceType: "domain",
            resourceId: domainId.ToString(),
            actor: user,
            httpContext: httpContext,
            oldValue: AuditLogEntryFactory.Snapshot(new
            {
                defaultRedirectUrl = entity.DefaultRedirectUrl,
                errorPageBranding = entity.ErrorPageBranding,
                notFoundBehavior = entity.NotFoundBehavior
            }),
            newValue: AuditLogEntryFactory.Snapshot(new
            {
                defaultRedirectUrl = updated.DefaultRedirectUrl,
                errorPageBranding = updated.ErrorPageBranding,
                notFoundBehavior = updated.NotFoundBehavior
            })));

        var response = MapToDetailResponse(updated);
        return Results.Ok(response);
    }

    /// <summary>Soft-deletes a domain. Links are disabled, the certificate is removed, and the hostname is held for a grace period.</summary>
    private static async Task<IResult> DeleteDomain(
        Guid tenantId,
        Guid domainId,
        IDomainRepository repository,
        IAuditLogService auditLog,
        ICertificateService certificateService,
        IDistributionService distributionService,
        ILogger<DomainEndpoints> logger,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var entity = await repository.GetByIdAsync(tenantId, domainId);
        if (entity is null)
            throw new NotFoundException("Domain", domainId.ToString());

        // Clean up the CloudFront distribution tenant if one was provisioned.
        if (!string.IsNullOrEmpty(entity.DistributionTenantId))
        {
            try
            {
                var deleted = await distributionService.DeleteDistributionTenantAsync(entity.DistributionTenantId);
                logger.LogInformation(
                    "Distribution tenant cleanup for domain {DomainId} {Hostname}: deleted={Deleted}",
                    domainId, entity.Hostname, deleted);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Distribution tenant cleanup failed for domain {DomainId} {Hostname}: {Message}",
                    domainId, entity.Hostname, ex.Message);
            }
        }

        // Clean up the ACM certificate if one was provisioned.
        if (!string.IsNullOrEmpty(entity.CertificateArn))
        {
            try
            {
                var deleted = await certificateService.DeleteCertificateAsync(entity.CertificateArn);
                logger.LogInformation(
                    "Certificate cleanup for domain {DomainId} {Hostname}: deleted={Deleted}",
                    domainId, entity.Hostname, deleted);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Certificate cleanup failed for domain {DomainId} {Hostname}: {Message}",
                    domainId, entity.Hostname, ex.Message);
            }
        }

        await repository.SoftDeleteAsync(tenantId, domainId);

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "domain.deleted",
            resourceType: "domain",
            resourceId: domainId.ToString(),
            actor: user,
            httpContext: httpContext,
            oldValue: AuditLogEntryFactory.Snapshot(new
            {
                hostname = entity.Hostname,
                status = entity.Status
            }),
            newValue: AuditLogEntryFactory.Snapshot(new { status = "deleted" })));

        return Results.NoContent();
    }

    /// <summary>
    /// Triggers DNS verification for a domain. On success, delegates to
    /// <see cref="IDomainActivationService"/> which provisions the ACM certificate,
    /// creates the CloudFront distribution tenant, and transitions the domain to
    /// <c>active</c> (or <c>certificate_failed</c> if certificate provisioning fails).
    /// Rate limited to 1 attempt per 60 seconds.
    /// </summary>
    private static async Task<IResult> VerifyDomain(
        Guid tenantId,
        Guid domainId,
        IDomainRepository repository,
        IDnsVerificationService dnsVerification,
        IDomainActivationService activationService,
        IAuditLogService auditLog,
        ILogger<DomainEndpoints> logger,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var entity = await repository.GetByIdAsync(tenantId, domainId);
        if (entity is null)
            throw new NotFoundException("Domain", domainId.ToString());

        if (entity.LastVerifiedAt is not null)
        {
            var elapsed = DateTime.UtcNow - entity.LastVerifiedAt.Value;
            if (elapsed < _verifyCooldown)
            {
                var retryAfter = (int)(_verifyCooldown - elapsed).TotalSeconds;
                throw new TooManyRequestsException(retryAfter);
            }
        }

        if (entity.Status is not "pending_verification" and not "verification_failed")
            throw new BadRequestException(
                $"Domain is in '{entity.Status}' status. Only domains in 'pending_verification' or 'verification_failed' status can be verified.");

        var verifying = entity with { Status = "verifying" };
        await repository.UpdateAsync(verifying);

        var txtValue = $"short-io-verify={entity.VerificationCode}";
        var dnsResult = await dnsVerification.VerifyAsync(entity.Hostname, txtValue, entity.CnameTarget);

        // Append this attempt to the rolling history (max 5 entries, oldest first).
        var attempt = new VerificationAttempt
        {
            AttemptedAt = DateTime.UtcNow,
            TxtPassed = dnsResult.TxtPassed,
            CnamePassed = dnsResult.CnamePassed,
            FailureReason = dnsResult.AllPassed ? null : dnsResult.TxtError ?? dnsResult.CnameError
        };
        var withAttempt = verifying with
        {
            VerificationAttempts = verifying.VerificationAttempts.TakeLast(4).Append(attempt).ToList()
        };

        DomainEntity finalDomain;
        string finalStatus;
        string message;

        if (dnsResult.AllPassed)
        {
            await auditLog.LogAsync(AuditLogEntryFactory.Create(
                tenantId,
                action: "domain.verified",
                resourceType: "domain",
                resourceId: domainId.ToString(),
                actor: user,
                httpContext: httpContext,
                oldValue: AuditLogEntryFactory.Snapshot(new { status = entity.Status }),
                newValue: AuditLogEntryFactory.Snapshot(new { status = "active" })));

            var actorId = user.Identity?.Name ?? "user";
            var activationResult = await activationService.ActivateAsync(withAttempt, actorId);
            finalDomain = activationResult.Domain;
            finalStatus = finalDomain.Status;
            message = activationResult.Succeeded
                ? "Domain verified and activated successfully."
                : $"DNS verification passed but activation failed: {activationResult.FailureReason}";

            logger.LogInformation(
                "VerifyDomain for {DomainId}: dns=pass activation={Succeeded} finalStatus={Status}",
                domainId, activationResult.Succeeded, finalStatus);
        }
        else
        {
            finalStatus = "verification_failed";
            var failed = withAttempt with
            {
                Status = finalStatus,
                LastVerifiedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await repository.UpdateAsync(failed);
            finalDomain = failed;
            message = "Verification failed. Check the DNS record details below.";

            await auditLog.LogAsync(AuditLogEntryFactory.Create(
                tenantId,
                action: "domain.verification_failed",
                resourceType: "domain",
                resourceId: domainId.ToString(),
                actor: user,
                httpContext: httpContext,
                oldValue: AuditLogEntryFactory.Snapshot(new { status = entity.Status }),
                newValue: AuditLogEntryFactory.Snapshot(new { status = finalStatus })));
        }

        var response = new VerifyDomainResponse
        {
            Id = entity.Id,
            Hostname = entity.Hostname,
            Status = finalStatus,
            TxtCheck = new DnsCheckDetail
            {
                Passed = dnsResult.TxtPassed,
                Expected = txtValue,
                Actual = dnsResult.ActualTxtValue,
                Error = dnsResult.TxtError
            },
            CnameCheck = new DnsCheckDetail
            {
                Passed = dnsResult.CnamePassed,
                Expected = entity.CnameTarget,
                Actual = dnsResult.ActualCnameValue,
                Error = dnsResult.CnameError
            },
            Message = message,
            CheckedAt = DateTime.UtcNow
        };

        return Results.Ok(response);
    }

    /// <summary>Requests an ACM certificate for a verified domain using DNS validation.</summary>
    private static async Task<IResult> RequestCertificate(
        Guid tenantId,
        Guid domainId,
        IDomainRepository repository,
        ICertificateService certificateService,
        IAuditLogService auditLog,
        ClaimsPrincipal user,
        HttpContext httpContext,
        ILogger<DomainEndpoints> logger)
    {
        var entity = await repository.GetByIdAsync(tenantId, domainId);
        if (entity is null)
            throw new NotFoundException("Domain", domainId.ToString());

        if (entity.Status != "active")
            throw new BadRequestException(
                $"Domain is in '{entity.Status}' status. Certificates can only be provisioned for active domains.");

        // 409 only when the customer has already explicitly provisioned a certificate
        // via this endpoint. An auto-provisioned certificate (from VerifyDomain) doesn't block
        // an explicit request so that customers can confirm the validation records.
        if (entity.CertificateExplicitlyProvisioned)
            throw new ConflictException(
                "A certificate has already been requested for this domain.");

        CertificateRequestResult result;
        try
        {
            result = await certificateService.RequestCertificateAsync(tenantId, domainId, entity.Hostname);
        }
        catch (InvalidOperationException ex)
        {
            throw new BadRequestException(ex.Message);
        }

        var updated = entity with
        {
            CertificateArn = result.CertificateArn,
            CertificateStatus = result.Status,
            CertificateExplicitlyProvisioned = true,
            UpdatedAt = DateTime.UtcNow
        };
        await repository.UpdateAsync(updated);

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "domain.certificate_requested",
            resourceType: "domain",
            resourceId: domainId.ToString(),
            actor: user,
            httpContext: httpContext,
            newValue: AuditLogEntryFactory.Snapshot(new
            {
                certificateArn = result.CertificateArn,
                certificateStatus = result.Status
            })));

        logger.LogInformation(
            "Certificate requested for domain {DomainId}: arn={Arn}",
            domainId, result.CertificateArn);

        return Results.Created(
            $"/tenants/{tenantId}/domains/{domainId}/certificate",
            new CertificateProvisionResponse
            {
                DomainId = domainId,
                CertificateArn = result.CertificateArn,
                Status = result.Status,
                ValidationRecords = result.ValidationRecords.Select(r => new CertificateRecord
                {
                    Name = r.Name,
                    Value = r.Value
                }).ToList()
            });
    }

    /// <summary>Gets the current status of the ACM certificate for a domain, including renewal eligibility.</summary>
    private static async Task<IResult> GetCertificateStatus(
        Guid tenantId,
        Guid domainId,
        IDomainRepository repository,
        ICertificateService certificateService)
    {
        var entity = await repository.GetByIdAsync(tenantId, domainId);
        if (entity is null)
            throw new NotFoundException("Domain", domainId.ToString());

        if (string.IsNullOrEmpty(entity.CertificateArn))
            throw new BadRequestException(
                "No certificate has been provisioned for this domain yet.");

        var status = await certificateService.GetCertificateStatusAsync(entity.CertificateArn);

        // Update the domain entity with the latest certificate status.
        if (entity.CertificateStatus != status.Status)
        {
            var updated = entity with
            {
                CertificateStatus = status.Status,
                UpdatedAt = DateTime.UtcNow
            };
            await repository.UpdateAsync(updated);
        }

        return Results.Ok(new CertificateStatusResponse
        {
            DomainId = domainId,
            CertificateArn = status.CertificateArn,
            Status = status.Status,
            IssuedAt = status.IssuedAt,
            ExpiresAt = status.ExpiresAt,
            IsRenewalEligible = status.IsRenewalEligible,
            FailureReason = status.FailureReason
        });
    }

    /// <summary>
    /// Deactivates a domain: marks all active links inactive, revokes the ACM certificate, and
    /// removes the CloudFront distribution tenant. The domain record is retained for 30 days so
    /// the customer can reactivate it. Cleanup failures are logged as warnings and do not block
    /// the status transition, allowing the customer to retry.
    /// </summary>
    private static async Task<IResult> DeactivateDomain(
        Guid tenantId,
        Guid domainId,
        IDomainRepository repository,
        ILinkRepository linkRepository,
        ICertificateService certificateService,
        IDistributionService distributionService,
        IAuditLogService auditLog,
        ILogger<DomainEndpoints> logger,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var entity = await repository.GetByIdAsync(tenantId, domainId);
        if (entity is null)
            throw new NotFoundException("Domain", domainId.ToString());

        if (entity.Status == "inactive")
            throw new ConflictException("Domain is already inactive.");

        if (entity.Status is not "active"
            and not "pending_verification"
            and not "verification_failed"
            and not "certificate_provisioning"
            and not "certificate_failed")
            throw new ConflictException(
                $"Cannot deactivate a domain in '{entity.Status}' status.");

        var now = DateTime.UtcNow;

        var updated = entity with
        {
            Status = "inactive",
            DeactivatedAt = now,
            UpdatedAt = now
        };
        await repository.UpdateAsync(updated);

        var deactivatedLinks = await linkRepository.DeactivateByDomainAsync(tenantId, domainId);
        logger.LogInformation(
            "Deactivated {Count} links for domain {DomainId} {Hostname}",
            deactivatedLinks, domainId, entity.Hostname);

        if (!string.IsNullOrEmpty(entity.DistributionTenantId))
        {
            try
            {
                var deleted = await distributionService.DeleteDistributionTenantAsync(entity.DistributionTenantId);
                logger.LogInformation(
                    "Distribution tenant removed for domain {DomainId} {Hostname}: deleted={Deleted}",
                    domainId, entity.Hostname, deleted);
                updated = updated with
                {
                    DistributionTenantId = null,
                    DistributionTenantStatus = null,
                    DistributionTenantETag = null
                };
                await repository.UpdateAsync(updated);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Distribution tenant removal failed for domain {DomainId} {Hostname}: {Message}",
                    domainId, entity.Hostname, ex.Message);
            }
        }

        if (!string.IsNullOrEmpty(entity.CertificateArn))
        {
            try
            {
                var deleted = await certificateService.DeleteCertificateAsync(entity.CertificateArn);
                logger.LogInformation(
                    "Certificate revoked for domain {DomainId} {Hostname}: deleted={Deleted}",
                    domainId, entity.Hostname, deleted);
                updated = updated with
                {
                    CertificateArn = null,
                    CertificateStatus = "revoked",
                    CertificateExplicitlyProvisioned = false
                };
                await repository.UpdateAsync(updated);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Certificate revocation failed for domain {DomainId} {Hostname}: {Message}",
                    domainId, entity.Hostname, ex.Message);
            }
        }

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "domain.deactivated",
            resourceType: "domain",
            resourceId: domainId.ToString(),
            actor: user,
            httpContext: httpContext,
            oldValue: AuditLogEntryFactory.Snapshot(new { status = entity.Status }),
            newValue: AuditLogEntryFactory.Snapshot(new
            {
                status = "inactive",
                deactivatedAt = now,
                linksDeactivated = deactivatedLinks
            })));

        return Results.Ok(MapToDetailResponse(updated));
    }

    /// <summary>
    /// Reactivates a deactivated domain within the 30-day grace period. Restores inactive links,
    /// re-provisions the ACM certificate, and recreates the CloudFront distribution tenant.
    /// Provisioning failures are logged but do not prevent the domain from returning to "active"
    /// status so the customer can trigger certificate/distribution setup through other endpoints.
    /// </summary>
    private static async Task<IResult> ReactivateDomain(
        Guid tenantId,
        Guid domainId,
        IDomainRepository repository,
        ILinkRepository linkRepository,
        ICertificateService certificateService,
        IDistributionService distributionService,
        IAuditLogService auditLog,
        ILogger<DomainEndpoints> logger,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var entity = await repository.GetByIdAsync(tenantId, domainId);
        if (entity is null)
            throw new NotFoundException("Domain", domainId.ToString());

        if (entity.Status != "inactive")
            throw new ConflictException(
                $"Domain is not inactive. Current status: '{entity.Status}'.");

        if (entity.DeactivatedAt.HasValue &&
            DateTime.UtcNow - entity.DeactivatedAt.Value > TimeSpan.FromDays(30))
            throw new ConflictException(
                "Domain cannot be reactivated: the 30-day grace period has expired.");

        var now = DateTime.UtcNow;

        var reactivatedLinks = await linkRepository.ReactivateByDomainAsync(tenantId, domainId);
        logger.LogInformation(
            "Reactivated {Count} links for domain {DomainId} {Hostname}",
            reactivatedLinks, domainId, entity.Hostname);

        var updated = entity with
        {
            Status = "active",
            DeactivatedAt = null,
            UpdatedAt = now
        };

        CertificateRequestResult? certResult = null;
        try
        {
            certResult = await certificateService.RequestCertificateAsync(tenantId, domainId, entity.Hostname);
            updated = updated with
            {
                CertificateArn = certResult.CertificateArn,
                CertificateStatus = certResult.Status
            };
            logger.LogInformation(
                "Certificate re-provisioned for domain {DomainId} {Hostname}: {Arn}",
                domainId, entity.Hostname, certResult.CertificateArn);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Certificate re-provisioning failed for domain {DomainId} {Hostname}: {Message}",
                domainId, entity.Hostname, ex.Message);
        }

        try
        {
            var quota = await distributionService.CheckQuotaAsync();
            if (!quota.IsWithinQuota)
            {
                logger.LogError(
                    "Distribution tenant quota exhausted during reactivation of domain {DomainId} {Hostname}: count={Count} limit={Limit}",
                    domainId, entity.Hostname, quota.CurrentCount, quota.QuotaLimit);
            }
            else
            {
                var distResult = await distributionService.CreateDistributionTenantAsync(
                    tenantId, domainId, entity.Hostname, updated.CertificateArn);
                updated = updated with
                {
                    DistributionTenantId = distResult.DistributionTenantId,
                    DistributionTenantStatus = distResult.Status,
                    DistributionTenantETag = distResult.ETag
                };
                logger.LogInformation(
                    "Distribution tenant re-created for domain {DomainId} {Hostname}: id={TenantId} status={Status}",
                    domainId, entity.Hostname, distResult.DistributionTenantId, distResult.Status);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Distribution tenant re-creation failed for domain {DomainId} {Hostname}: {Message}",
                domainId, entity.Hostname, ex.Message);
        }

        await repository.UpdateAsync(updated);

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "domain.reactivated",
            resourceType: "domain",
            resourceId: domainId.ToString(),
            actor: user,
            httpContext: httpContext,
            oldValue: AuditLogEntryFactory.Snapshot(new { status = entity.Status }),
            newValue: AuditLogEntryFactory.Snapshot(new
            {
                status = "active",
                linksReactivated = reactivatedLinks
            })));

        return Results.Ok(MapToDetailResponse(updated));
    }

    /// <summary>
    /// Returns a lightweight status snapshot derived entirely from stored entity state.
    /// No live DNS or HTTP probes are performed, so this is safe to poll frequently.
    /// </summary>
    private static async Task<IResult> GetStatus(
        Guid tenantId,
        Guid domainId,
        IDomainRepository repository)
    {
        var entity = await repository.GetByIdAsync(tenantId, domainId);
        if (entity is null)
            throw new NotFoundException("Domain", domainId.ToString());

        var txtValue = $"short-io-verify={entity.VerificationCode}";

        var response = new DomainStatusResponse
        {
            DomainId = entity.Id,
            Hostname = entity.Hostname,
            Status = entity.Status,
            HealthScore = ComputeHealthScore(entity),
            Verification = new DomainVerificationStatus
            {
                Status = entity.Status,
                LastAttemptAt = entity.LastVerifiedAt,
                DnsRecords = new DnsRecordExpectations
                {
                    TxtName = entity.Hostname,
                    TxtValue = txtValue,
                    CnameName = entity.Hostname,
                    CnameTarget = entity.CnameTarget
                },
                RecentAttempts = entity.VerificationAttempts
                    .Select(a => new VerificationAttemptSummary
                    {
                        AttemptedAt = a.AttemptedAt,
                        Passed = a.TxtPassed && a.CnamePassed,
                        TxtPassed = a.TxtPassed,
                        CnamePassed = a.CnamePassed,
                        FailureReason = a.FailureReason
                    })
                    .ToList()
                    .AsReadOnly()
            },
            Certificate = new DomainCertificateSummary
            {
                Status = entity.CertificateStatus,
                Arn = entity.CertificateArn
            },
            Routing = new DomainRoutingStatus
            {
                RoutingEndpoint = entity.CnameTarget,
                DistributionTenantId = entity.DistributionTenantId,
                DistributionStatus = entity.DistributionTenantStatus,
                IsServing = entity.Status == "active" && entity.DistributionTenantStatus == "Deployed"
            },
            LinkCount = entity.LinkCount,
            SuggestedActions = BuildSuggestedActions(entity),
            CheckedAt = DateTime.UtcNow
        };

        return Results.Ok(response);
    }

    private static string ComputeHealthScore(DomainEntity entity) =>
        entity.Status switch
        {
            "verification_failed" or "certificate_failed" or "inactive" => "red",
            "active" when entity.CertificateStatus == "issued" && entity.DistributionTenantStatus == "Deployed" => "green",
            _ => "yellow"
        };

    private static IReadOnlyList<SuggestedAction> BuildSuggestedActions(DomainEntity entity)
    {
        var actions = new List<SuggestedAction>();

        switch (entity.Status)
        {
            case "pending_verification":
            case "verifying":
                actions.Add(new SuggestedAction
                {
                    ActionCode = "add_dns_records",
                    Label = "Add DNS Records",
                    Description = $"Create the required TXT and CNAME records at your DNS provider to prove ownership of {entity.Hostname}."
                });
                actions.Add(new SuggestedAction
                {
                    ActionCode = "trigger_verification",
                    Label = "Verify Domain",
                    Description = "Once DNS records are in place, trigger verification to activate your domain."
                });
                break;

            case "verification_failed":
                actions.Add(new SuggestedAction
                {
                    ActionCode = "check_dns_records",
                    Label = "Check DNS Records",
                    Description = "Confirm the TXT and CNAME records are correctly set at your DNS provider and match the expected values shown below."
                });
                actions.Add(new SuggestedAction
                {
                    ActionCode = "retry_verification",
                    Label = "Retry Verification",
                    Description = "After confirming the records are correct, retry verification to activate your domain."
                });
                break;

            case "certificate_provisioning":
                actions.Add(new SuggestedAction
                {
                    ActionCode = "wait_for_certificate",
                    Label = "Wait for Certificate",
                    Description = "Your TLS certificate is being provisioned by AWS ACM. This typically completes within a few minutes. No action is required."
                });
                break;

            case "certificate_failed":
                actions.Add(new SuggestedAction
                {
                    ActionCode = "reprovision_certificate",
                    Label = "Request New Certificate",
                    Description = "Certificate provisioning failed. Request a new certificate and ensure the ACM validation CNAME records are present in your DNS."
                });
                break;

            case "inactive":
                actions.Add(new SuggestedAction
                {
                    ActionCode = "reactivate_domain",
                    Label = "Reactivate Domain",
                    Description = "Reactivate this domain within the 30-day grace period to restore links, re-provision the certificate, and recreate the CloudFront distribution."
                });
                break;

            case "active":
                if (entity.CertificateStatus != "issued")
                    actions.Add(new SuggestedAction
                    {
                        ActionCode = "provision_certificate",
                        Label = "Request Certificate",
                        Description = "Your domain is active but has no issued TLS certificate. Request a certificate to enable HTTPS for all short links."
                    });
                break;
        }

        return actions.AsReadOnly();
    }

    /// <summary>
    /// Runs a full suite of diagnostic checks for the domain: DNS records, TLS certificate,
    /// CloudFront distribution, end-to-end HTTPS connectivity, and multi-resolver propagation.
    /// </summary>
    private static async Task<IResult> GetDiagnostics(
        Guid tenantId,
        Guid domainId,
        IDomainRepository repository,
        IDomainDiagnosticsService diagnosticsService,
        CancellationToken ct)
    {
        var entity = await repository.GetByIdAsync(tenantId, domainId);
        if (entity is null)
            throw new NotFoundException("Domain", domainId.ToString());

        var report = await diagnosticsService.RunAsync(entity, ct);

        var failCount = report.Checks.Count(c => c.Status == DiagnosticStatus.Fail);
        var warnCount = report.Checks.Count(c => c.Status == DiagnosticStatus.Warning);
        var passCount = report.Checks.Count(c => c.Status == DiagnosticStatus.Pass);
        var totalApplicable = report.Checks.Count(c => c.Status != DiagnosticStatus.Skip);

        var overallStatus = failCount > 0 ? "fail"
            : warnCount > 0 ? "partial"
            : "pass";

        var summary = overallStatus switch
        {
            "pass"    => $"All {passCount} checks passed. Domain is correctly configured.",
            "fail"    => $"{failCount} check{(failCount == 1 ? "" : "s")} failed out of {totalApplicable}. Review the detected issues below for remediation steps.",
            _         => $"{passCount} checks passed, {warnCount} warning{(warnCount == 1 ? "" : "s")} out of {totalApplicable} applicable checks. Review warnings to ensure optimal service."
        };

        return Results.Ok(new DomainDiagnosticsResponse
        {
            DomainId = domainId,
            Hostname = entity.Hostname,
            OverallStatus = overallStatus,
            Checks = report.Checks
                .Select(c => new DiagnosticCheckResponse
                {
                    Name = c.Name,
                    Label = c.Label,
                    Status = c.Status.ToString().ToLowerInvariant(),
                    Message = c.Message,
                    Details = c.Details
                })
                .ToList()
                .AsReadOnly(),
            DetectedIssues = report.Issues
                .Select(i => new DiagnosticIssueResponse
                {
                    Code = i.Code,
                    Severity = i.Severity,
                    Title = i.Title,
                    Description = i.Description,
                    Remediation = i.Remediation
                })
                .ToList()
                .AsReadOnly(),
            Summary = summary,
            RunAt = report.RunAt
        });
    }

    private static DomainDetailResponse MapToDetailResponse(DomainEntity entity)
    {
        return new DomainDetailResponse
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            Hostname = entity.Hostname,
            Status = entity.Status,
            CertificateStatus = entity.CertificateStatus,
            CertificateArn = entity.CertificateArn,
            LinkCount = entity.LinkCount,
            Settings = new DomainSettings
            {
                DefaultRedirectUrl = entity.DefaultRedirectUrl,
                ErrorPageBranding = entity.ErrorPageBranding,
                NotFoundBehavior = entity.NotFoundBehavior
            },
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            DeletedAt = entity.DeletedAt,
            DeactivatedAt = entity.DeactivatedAt,
            DistributionTenantId = entity.DistributionTenantId,
            DistributionTenantStatus = entity.DistributionTenantStatus
        };
    }

    private static string GenerateVerificationCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
