# CloudFront SaaS Manager API — Research & Integration Reference

**Date:** 2026-06-03
**Purpose:** Technical reference for programmatic distribution tenant management via CloudFront SaaS Manager APIs. Extends [ADR-001: CloudFront SaaS Manager for Domain Management](../decisions/ADR-001-cloudfront-saas-manager.md).

---

## API Availability & Status

CloudFront SaaS Manager APIs were released **April 28, 2025** as part of a major CloudFront service update introducing multi-tenant distribution capabilities. As of June 2026, these APIs are **generally available** in all commercial AWS regions where CloudFront operates.

- **25 new API methods** and **17 updated methods** added in the April 2025 release
- Available in **AWS SDKs** (all languages), **AWS CLI**, **CloudFormation**, **CDK**, and **Terraform** (AWS provider v6.28.0+)
- CloudFront global service — no per-region deployment; API calls target `cloudfront.amazonaws.com` (us-east-1 endpoint)

### Stability Assessment

| Factor | Status |
|---|---|
| API maturity | 14 months since launch, no breaking changes observed |
| Terraform support | Available since provider v6.28.0 (Jan 2026), stable but one known bug (#46302 with `custom_error_response`) |
| CDK L2 constructs | Not yet — L1 `Cfn*` resources only as of CDK 2.209.0 |
| Production references | AWS blogs in 2025–2026 show production use; limited third-party case studies |
| SDK parity | All official AWS SDKs supported; .NET SDK 4.0.0.0+ includes full API coverage |

**Verdict:** Stable enough for production use with Terraform. Monitor the [hashicorp/terraform-provider-aws#46302](https://github.com/hashicorp/terraform-provider-aws/issues/46302) issue when using `custom_error_response` on multi-tenant distributions.

---

## Architecture Overview

CloudFront SaaS Manager introduces three new resource types layered on top of CloudFront:

```
Connection Group          ──  Routing endpoint (CNAME target), Anycast IP lists
    │
Multi-Tenant Distribution  ──  Reusable config template: origins, cache behaviors,
    │                          parameter definitions, security policies
    │
Distribution Tenant       ──  Per-customer domain slice: domains, certificates,
                               WAF overrides, geo-restrictions, parameter values
```

**Key concept:** The Multi-Tenant Distribution is a **template** — it defines the shared infrastructure (origins, caching, behaviors). Distribution Tenants are **instances** that inherit the template but can override TLS certificates, WAF rules, and geo-restrictions per customer domain.

### Resource Relationships

| Resource | Max per account | Description |
|---|---|---|
| **Multi-Tenant Distributions** | 20 | Template distributions with `connection_mode = "tenant-only"` |
| **Connection Groups** | 100 | Defines routing endpoints (CNAME targets) and Anycast IPs |
| **Distribution Tenants** | 10,000 (default) | One per customer domain; inherits from the parent distribution |
| **Anycast Static IPs per Connection Group** | 5 | For IP-allowlisting enterprise customers |

### Parameter System

Multi-tenant distributions support up to **5 parameter definitions** that tenants populate with their own values. Parameters use `{{parameterName}}` syntax in distribution configuration fields (origin domain, origin path, etc.), enabling tenant-specific routing without per-tenant origin configuration changes.

---

## API Operations

### Tenant CRUD

| Operation | HTTP Method | Description |
|---|---|---|
| `CreateDistributionTenant` | POST | Create a tenant under a multi-tenant distribution |
| `GetDistributionTenant` | GET | Get tenant by ID |
| `GetDistributionTenantByDomain` | GET | Look up tenant by domain name |
| `UpdateDistributionTenant` | PUT | Update tenant domains, customizations, parameters; requires ETag |
| `DeleteDistributionTenant` | DELETE | Remove a tenant |
| `ListDistributionTenants` | GET | List tenants for a distribution (paginated) |

### Connection Groups

| Operation | Description |
|---|---|
| `CreateConnectionGroup` | Create a connection group with routing endpoint |
| `GetConnectionGroup` | Get connection group details including routing endpoint |
| `UpdateConnectionGroup` | Update connection group configuration |
| `DeleteConnectionGroup` | Remove a connection group |
| `ListConnectionGroups` | List connection groups |

### Tenant-Specific Operations

| Operation | Description |
|---|---|
| `AssociateDistributionTenantWebACL` | Attach a WAF web ACL to a tenant |
| `DisassociateDistributionTenantWebACL` | Detach a WAF web ACL from a tenant |
| `CreateInvalidationForDistributionTenant` | Purge cached objects for a specific tenant |

### Entity IDs

All tenant IDs follow the pattern: `dt_<base62-encoded>` (e.g., `dt_2zhRB0vBe0B72LZCvy1mgzI1AB`). Connection group IDs follow the pattern: `cg_<base62-encoded>`.

---

## Quotas and Limits

| Quota | Default | Hard Limit | Requestable Increase |
|---|---|---|---|
| Distribution Tenants per account | 10,000 | No hard ceiling | Yes — via Service Quotas or AWS Support |
| Multi-Tenant Distributions per account | 20 | — | Yes |
| Connection Groups per account | 100 | — | Yes |
| Domains per Distribution Tenant | 5 | 5 | **No** — hard limit |
| Anycast Static IPs per Connection Group | 5 | — | Yes |
| Parameters per Multi-Tenant Distribution | 5 | 5 | **No** — hard limit |
| Parameters per Distribution Tenant | 5 | — | Set by parent distribution definition |
| Parameters in a single configuration field | 2 | 2 | **No** — hard limit |
| Requests per second (distribution-level) | — | Same as standard CloudFront | — |

### Quota Increase Process

1. **Preferred path:** Use the **Service Quotas console** or AWS CLI:
   ```bash
   aws service-quotas request-service-quota-increase \
       --service-code cloudfront \
       --quota-code L-XXXXXXXX \
       --desired-value 50000
   ```
2. **Fallback:** If the quota isn't listed in Service Quotas, open a case via **AWS Support Center** → "Create case" → "Service limit increase" → CloudFront → "Distribution tenants per account".
3. **Lead time:** Standard quota increases are typically approved within 24–48 hours. Plan increases when approaching 80% of the current limit (8,000 tenants at default).
4. **Multi-account sharding** should be designed from day one as a scaling escape hatch (see Integration Design below).

---

## Authentication & IAM Permissions

All CloudFront SaaS Manager APIs use standard AWS Signature v4 authentication — the same as all other CloudFront API calls. No separate auth mechanism.

### Minimal IAM Policy for Tenant Management

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "cloudfront:CreateDistributionTenant",
        "cloudfront:GetDistributionTenant",
        "cloudfront:GetDistributionTenantByDomain",
        "cloudfront:ListDistributionTenants",
        "cloudfront:UpdateDistributionTenant",
        "cloudfront:DeleteDistributionTenant"
      ],
      "Resource": "arn:aws:cloudfront::ACCOUNT_ID:distribution-tenant/*"
    },
    {
      "Effect": "Allow",
      "Action": [
        "cloudfront:GetDistribution",
        "cloudfront:GetMultiTenantDistribution",
        "cloudfront:GetConnectionGroup"
      ],
      "Resource": "*"
    },
    {
      "Effect": "Allow",
      "Action": [
        "cloudfront:AssociateDistributionTenantWebACL",
        "cloudfront:DisassociateDistributionTenantWebACL"
      ],
      "Resource": "*"
    },
    {
      "Effect": "Allow",
      "Action": [
        "cloudfront:CreateInvalidationForDistributionTenant"
      ],
      "Resource": "arn:aws:cloudfront::ACCOUNT_ID:distribution-tenant/*"
    },
    {
      "Effect": "Allow",
      "Action": [
        "acm:RequestCertificate",
        "acm:DescribeCertificate",
        "acm:DeleteCertificate",
        "acm:ListCertificates"
      ],
      "Resource": "arn:aws:acm:us-east-1:ACCOUNT_ID:certificate/*"
    }
  ]
}
```

**Important:** ACM certificates used with CloudFront SaaS Manager **must** be created in `us-east-1` (CloudFront is a global service and ACM certs for CloudFront must live in us-east-1). This applies to both manually provisioned and CloudFront-managed certificates.

---

## SDK Support

### Terraform (AWS Provider v6.28.0+, Jan 2026)

Resources introduced:
- `aws_cloudfront_multitenant_distribution` — parent multi-tenant distribution
- `aws_cloudfront_distribution_tenant` — per-customer tenant
- `aws_cloudfront_connection_group` — routing endpoints
- Data sources: `aws_cloudfront_connection_group`, `aws_cloudfront_distribution_tenant`

### CloudFormation / CDK (L1)

Resources available as L1 `Cfn*` types:
- `AWS::CloudFront::DistributionTenant` / `CfnDistributionTenant`
- `AWS::CloudFront::ConnectionGroup` / `CfnConnectionGroup`

CDK L2 constructs are **not yet available** as of CDK 2.209.0. Use L1 resources directly or write custom L2 wrappers.

### AWS Cloud Control Provider (Terraform awscc)

The `awscc` provider has `awscc_cloudfront_distribution_tenant` as both resource and data source, providing an alternative when the standard AWS provider has gaps.

### .NET SDK

The `Amazon.CloudFront` namespace in AWSSDK.CloudFront (v3.7+) includes:
- `CreateDistributionTenantAsync` / `CreateDistributionTenantRequest`
- `GetDistributionTenantAsync` / `GetDistributionTenantRequest`
- `UpdateDistributionTenantAsync` / `UpdateDistributionTenantRequest`
- `DeleteDistributionTenantAsync` / `DeleteDistributionTenantRequest`
- `ListDistributionTenantsAsync` / `ListDistributionTenantsRequest`

### AWS CLI (v2)

Full support: `aws cloudfront create-distribution-tenant`, `aws cloudfront update-distribution-tenant`, etc.

---

## Code Examples

### 1. Create a Distribution Tenant (CLI)

```bash
aws cloudfront create-distribution-tenant \
    --distribution-id EDVD632BHDS5 \
    --name "customer-123" \
    --domains '[{"Domain":"go.customer.com"}]' \
    --connection-group-id cg_abc123def456 \
    --enabled true \
    --parameters '[{"Name":"tenantName","Value":"customer-123"}]' \
    --tags '{"Items":[{"Key":"TenantId","Value":"550e8400-e29b-41d4-a716-446655440000"}]}'
```

### 2. Create a Tenant with Custom Certificate (CLI)

```bash
aws cloudfront create-distribution-tenant \
    --distribution-id EDVD632BHDS5 \
    --name "customer-456" \
    --domains '[{"Domain":"links.customer456.com"}]' \
    --connection-group-id cg_abc123def456 \
    --enabled true \
    --customizations '{
        "Certificate": {
            "Arn": "arn:aws:acm:us-east-1:123456789012:certificate/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
        },
        "WebAcl": {
            "Action": "disable"
        },
        "GeoRestrictions": {
            "RestrictionType": "blacklist",
            "Locations": ["RU", "CN", "KP", "IR"]
        }
    }'
```

### 3. Create a Tenant with CloudFront-Managed Certificate (CLI)

```bash
aws cloudfront create-distribution-tenant \
    --distribution-id EDVD632BHDS5 \
    --name "customer-789" \
    --domains '[{"Domain":"app.customer789.com"}]' \
    --connection-group-id cg_abc123def456 \
    --enabled true \
    --managed-certificate-request '{
        "ValidationTokenHost": "cloudfront",
        "PrimaryDomainName": "app.customer789.com",
        "CertificateTransparencyLoggingPreference": "enabled"
    }'
```

**Important:** `ValidationTokenHost: "cloudfront"` means CloudFront serves the ACM validation token automatically — no customer action needed beyond setting the CNAME DNS record. `ValidationTokenHost: "self-hosted"` requires the customer to serve the validation token from their own infrastructure before switching DNS.

### 4. .NET SDK — Create Tenant

```csharp
using Amazon.CloudFront;
using Amazon.CloudFront.Model;

var client = new AmazonCloudFrontClient();

var request = new CreateDistributionTenantRequest
{
    DistributionId = "EDVD632BHDS5",
    Name = $"tenant-{tenantId:N}",
    Enabled = false, // disabled until certificate validates
    Domains = [new DomainItem { Domain = domainName }],
    ConnectionGroupId = connectionGroupId,
    Customizations = new TenantCustomizations
    {
        WebAcl = new WebAclCustomization { Action = "disable" }
    },
    Parameters =
    [
        new Parameter { Name = "tenantName", Value = tenantId.ToString("N") }
    ],
    Tags =
    [
        new Tag { Key = "TenantId", Value = tenantId.ToString() },
        new Tag { Key = "Environment", Value = environment },
        new Tag { Key = "Service", Value = "url-shortener" }
    ]
};

var response = await client.CreateDistributionTenantAsync(request);
var tenant = response.DistributionTenant;

// Poll for status
while (tenant.Status == "InProgress")
{
    await Task.Delay(TimeSpan.FromSeconds(5));
    var getResponse = await client.GetDistributionTenantAsync(
        new GetDistributionTenantRequest { Id = tenant.Id });
    tenant = getResponse.DistributionTenant;
}

if (tenant.Status == "Deployed")
{
    // Enable traffic
    await client.UpdateDistributionTenantAsync(
        new UpdateDistributionTenantRequest
        {
            Id = tenant.Id,
            DistributionId = "EDVD632BHDS5",
            IfMatch = tenant.ETag,
            Enabled = true
        });
}
```

### 5. Terraform — Full Integration

```hcl
# ── Connection Group ────────────────────────────────────────────────────────
resource "aws_cloudfront_connection_group" "main" {
  name = "url-shortener-${var.environment}"
}

# ── Multi-Tenant Distribution ────────────────────────────────────────────────
resource "aws_cloudfront_multitenant_distribution" "redirect" {
  name    = "redirect-${var.environment}"
  enabled = true

  # Inherited origins, cache behaviors, security headers, etc.
  # Configuration mirrors the existing aws_cloudfront_distribution module pattern.

  connection_group_id = aws_cloudfront_connection_group.main.id

  tags = var.common_tags
}

# ── Tenant (one per customer domain) ─────────────────────────────────────────
resource "aws_cloudfront_distribution_tenant" "tenant" {
  for_each = var.customer_domains

  name            = "tenant-${each.value.tenant_id}"
  distribution_id = aws_cloudfront_multitenant_distribution.redirect.id

  domain {
    domain = each.key
  }

  parameter {
    key   = "tenantName"
    value = each.value.tenant_id
  }

  enabled = false # enable after certificate validates

  tags = merge(var.common_tags, {
    TenantId = each.value.tenant_id
  })
}
```

### 6. Get Routing Endpoint

```bash
aws cloudfront get-connection-group \
    --id cg_abc123def456 \
    --query 'ConnectionGroup.RoutingEndpoint' \
    --output text
# Output: d1234abcd5678.cloudfront.net
```

The routing endpoint is the CNAME target for all tenant domains. Customer DNS configuration:

```
go.customer.com.  CNAME  d1234abcd5678.cloudfront.net.
```

---

## Certificate Automation (ACM Integration)

CloudFront SaaS Manager supports three certificate provisioning models:

### A. CloudFront-Managed Certificates (Recommended)

- **`ValidationTokenHost: "cloudfront"`** — CloudFront automatically serves the ACM validation token at the well-known path. Customer only needs to set the CNAME pointing their domain to the connection group routing endpoint. Simplest option — no customer-side certificate management.

- **`ValidationTokenHost: "self-hosted"`** — CloudFront generates the ACM validation token but the **customer** must serve it from their existing infrastructure before DNS is switched. Useful when the domain is already serving traffic and downtime must be avoided.

### B. Bring Your Own Certificate (BYOC)

Customer provisions an ACM certificate in their own AWS account, then shares it with the platform account via ACM cross-account sharing, or the platform provisions the certificate directly with the customer's domain ownership verified via DNS validation (email validation is not supported for CloudFront).

### C. Platform-Provisioned Certificate

The platform's AWS account provisions the ACM certificate using DNS validation:

1. Create ACM certificate in `us-east-1`
2. ACM generates a DNS validation CNAME record
3. Customer adds the validation CNAME record to their DNS zone
4. ACM validates domain ownership (typically 5–30 minutes)
5. Attach the validated certificate to the distribution tenant

**The platform should prefer CloudFront-managed certificates (option A) with `ValidationTokenHost: "cloudfront"`** for new tenant domains — it eliminates certificate lifecycle management entirely.

---

## DNS Validation Flow

### Standard Onboarding Flow (CloudFront-Managed Certificate)

```
1. Platform creates Distribution Tenant with:
     - Domain: go.customer.com
     - ManagedCertificateRequest { ValidationTokenHost: "cloudfront" }
     - Enabled: false

2. Tenant enters "InProgress" status — CloudFront begins ACM validation.

3. Platform retrieves routing endpoint:
     aws cloudfront get-connection-group --id cg_main
     → d1234abcd5678.cloudfront.net

4. Platform instructs customer (or Route53 automation) to set:
     go.customer.com.  CNAME  d1234abcd5678.cloudfront.net.
     TTL: 300

5. CloudFront detects the CNAME → ACM validation succeeds → certificate issued.
   Tenant status transitions: InProgress → Deployed.

6. Platform enables the tenant (Enabled: true) → tenant serves traffic.
```

### BYOC Flow

```
1. Customer provides ACM certificate ARN from their account (us-east-1).

2. Platform creates Distribution Tenant with:
     - Domain: go.customer.com
     - Customizations.Certificate.Arn: arn:aws:acm:us-east-1:...
     - Enabled: false

3. Platform instructs customer to set CNAME to routing endpoint.

4. Platform validates CNAME presence → enables tenant.
```

### Polling for Status

```csharp
// Poll until the tenant is deployed (max 10 minutes)
async Task<DistributionTenant> WaitForDeploymentAsync(
    AmazonCloudFrontClient client, string tenantId, CancellationToken ct)
{
    var deadline = DateTime.UtcNow.AddMinutes(10);
    while (DateTime.UtcNow < deadline)
    {
        var getResponse = await client.GetDistributionTenantAsync(
            new GetDistributionTenantRequest { Id = tenantId }, ct);
        var tenant = getResponse.DistributionTenant;

        if (tenant.Status == "Deployed") return tenant;
        if (tenant.Status == "Failed")
            throw new DomainProvisioningException(
                $"Tenant {tenantId} provisioning failed. Check ACM validation.");

        await Task.Delay(TimeSpan.FromSeconds(5), ct);
    }
    throw new TimeoutException($"Tenant {tenantId} did not deploy within 10 minutes.");
}
```

---

## Routing Endpoint Pattern

### DNS Architecture

```
                            ┌────────────────────────────┐
  go.customer1.com  CNAME   │                            │
  links.customer2.com CNAME │  Connection Group           │
  app.customer3.com  CNAME  │  Routing Endpoint           │
                            │  d1234.cloudfront.net       │
                            │                            │
                            │  ┌──────────────────────┐  │
                            │  │ Multi-Tenant Dist     │  │
                            │  │ (shared config)       │  │
                            │  │                      │  │
                            │  │  Tenant: customer1   │  │
                            │  │  Tenant: customer2   │  │
                            │  │  Tenant: customer3   │  │
                            │  └──────────────────────┘  │
                            └────────────────────────────┘
```

### Critical Constraint: CNAME Only

**CloudFront SaaS Manager routing endpoints do not support Route 53 ALIAS records (A/AAAA).** This means:

- **Subdomains work:** `go.example.com`, `links.example.com` — use CNAME records.
- **Zone apex domains do NOT work:** `example.com` cannot use a CNAME (violates DNS RFC 1034), and ALIAS records are not recognized by the SaaS Manager's certificate validation pipeline. See Limitations section below for workarounds.

---

## Limitations and Workarounds

### 1. Zone Apex Domain Support (CRITICAL)

**Limitation:** CloudFront SaaS Manager does not support Route 53 ALIAS records. Zone apex domains (`example.com`) cannot be onboarded because DNS standards forbid CNAMEs at the apex, and ALIAS records fail the SaaS Manager's domain validation.

**Status:** Known issue, reported on AWS re:Post. No official fix as of June 2026.

**Workarounds:**

| Approach | Feasibility | Trade-off |
|---|---|---|
| **Subdomain-only** (`go.example.com`) | Best | Customer must use subdomain; not all customers accept this |
| **www + apex redirect** — `www.example.com` via CNAME, apex (ALIAS) redirects to `www` | Good | Requires a separate standard CloudFront distribution or S3 redirect bucket for apex; adds latency and cost |
| **Standard CloudFront distribution per apex domain** | Manual | Falls back to pre-SaaS-Manager model; works but loses tenant management automation |
| **Manual ACM validation** — provision certificate outside SaaS Manager, attach as custom cert | Works | Certificate lifecycle is your responsibility; more operational burden |

**Recommendation for this project:** Accept subdomain-only for the initial release. Add an apex redirect service (separate CloudFront distribution with ALIAS support) if customer demand requires it. Design the provisioning API to accept a "domain type" parameter so apex support can be added without API changes.

### 2. Terraform Provider Bug

**Limitation:** `aws_cloudfront_multitenant_distribution` with `custom_error_response` produces inconsistent results after apply ([hashicorp/terraform-provider-aws#46302](https://github.com/hashicorp/terraform-provider-aws/issues/46302)). Null vs. empty string handling causes drift detection issues.

**Workaround:** Define `custom_error_response` blocks in the `aws_cloudfront_multitenant_distribution` only when they contain non-null values. Use lifecycle `ignore_changes` if drift is observed:

```hcl
lifecycle {
  ignore_changes = [custom_error_response]
}
```

### 3. No CDK L2 Constructs

**Limitation:** CDK only provides L1 `Cfn*` resources (auto-generated from CloudFormation). No high-level L2 constructs with ergonomic defaults and helper methods.

**Workaround:** Use L1 constructs or Terraform. Write a thin wrapper class for common tenant creation patterns. L2 constructs will likely be added by AWS in a future CDK release — track the CDK GitHub repo.

### 4. Tenant Status Polling Required

**Limitation:** Tenant creation (especially with managed certificates) is asynchronous. There is no EventBridge event or SNS notification for status changes; polling `GetDistributionTenant` every ~5 seconds is the only option.

**Workaround:** Wrap the polling loop in the provisioning service. Add a configurable timeout (10 minutes default). For high-volume onboarding, batch polling can check multiple tenants in a single `ListDistributionTenants` call.

### 5. ETag-Based Optimistic Locking

**Limitation:** `UpdateDistributionTenant` requires the `ETag` value from `GetDistributionTenant` as an `IfMatch` parameter. Concurrent updates will fail with a 412 Precondition Failed.

**Workaround:** Design the provisioning service to read-before-write and retry on 412. This is standard CloudFront API behavior (same as `UpdateDistribution`); not unique to SaaS Manager.

### 6. Domains per Tenant Hard Cap

**Limitation:** Maximum 5 domains per distribution tenant. Cannot increase.

**Workaround:** If a customer needs more than 5 domains, create multiple distribution tenants and group them logically. For the branded link use case (one domain per customer), this is not a constraint.

---

## Integration Design

### Provisioning Service Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                    Domain Provisioning Service                        │
│                                                                     │
│  ControlPlane API ──► DomainService ──► CloudFrontSaaSManagerClient  │
│       │                     │                    │                  │
│       │                     │                    ├─ CreateTenant    │
│       │                     │                    ├─ GetTenant       │
│       │                     │                    ├─ UpdateTenant    │
│       │                     │                    ├─ DeleteTenant    │
│       │                     │                    └─ PollStatus      │
│       │                     │                                       │
│       │                     └─► DnsVerificationService               │
│       │                              ├─ Check CNAME propagation     │
│       │                              └─ Route53 automation          │
│       │                                                             │
│  State machine ──► Poll tenant status ──► Enable when deployed       │
│  (Step Functions                                                      │
│   or hosted                                                          │
│   background svc)                                                    │
└─────────────────────────────────────────────────────────────────────┘
```

### Multi-Account Sharding (10,000+ Tenants)

The default quota of 10,000 distribution tenants per account may become a constraint. Design the provisioning layer with an account router from day one:

```csharp
public interface ICloudFrontAccountRouter
{
    /// <summary>Returns the AWS account ID for the given tenant.</summary>
    /// Uses consistent hashing on tenant ID so the same tenant always lands
    /// in the same account. Avoids tenant-move complexity.
    string ResolveAccount(Guid tenantId);
}
```

With 2 accounts, capacity doubles to 20,000 tenants; with 5 accounts, 50,000 — all using the same provisioning logic.

### Tenant Lifecycle State Machine

```
                    ┌──────────┐
                    │  Create  │
                    └────┬─────┘
                         │
                    ┌────▼─────┐
                    │Pending    │ (InProgress, cert validating)
                    │Validation │
                    └────┬─────┘
                         │
              ┌──────────┼──────────┐
              │          │          │
         ┌────▼───┐ ┌────▼───┐ ┌───▼────┐
         │ Active │ │Suspended│ │ Failed │ (max 10min timeout, retryable)
         └────┬───┘ └────┬───┘ └────────┘
              │          │
         ┌────▼───┐      │
         │Deleted  │◄─────┘
         │(soft)   │
         └─────────┘
```

### Data Model (ControlPlane → CloudFront mapping)

```csharp
public record DomainEntity
{
    public required Guid Id { get; init; }
    public required Guid TenantId { get; init; }
    public required string Hostname { get; init; }

    // CloudFront SaaS Manager correlation
    public string? DistributionTenantId { get; init; }     // dt_abc123...
    public string? DistributionTenantStatus { get; init; } // Pending, Deployed, Failed
    public string? AcmCertificateArn { get; init; }
    public bool ManagedCertificate { get; init; }          // true = CloudFront-managed cert
    public DateTime? ProvisionedAt { get; init; }
    public DateTime? ValidatedAt { get; init; }
}
```

### Implementation Sequence

1. **Phase 1: Terraform baseline** — Deploy the connection group and multi-tenant distribution via Terraform. Validate the routing endpoint works with a manual tenant.

2. **Phase 2: Tenant management API** — Implement domain provisioning in the ControlPlane API:
   - `POST /tenants/{tenantId}/domains` — creates a distribution tenant + DNS instructions
   - `GET /tenants/{tenantId}/domains/{domainId}/provisioning` — returns status, DNS instructions, routing endpoint
   - `DELETE /tenants/{tenantId}/domains/{domainId}` — deletes the distribution tenant

3. **Phase 3: Status polling** — Hosted background service or Step Functions state machine that polls tenant status during provisioning.

4. **Phase 4: Account sharding** — When approaching 8,000 tenants (80% of default quota), implement multi-account routing.

### Testing Strategy

- **Sandbox environment:** A dedicated `sandbox` AWS account with its own connection group + multi-tenant distribution. Terraform `sandbox/` environment deploys both.
- **Domain provisioning tests:** Integration tests that create/delete distribution tenants against the sandbox account, verifying status transitions and routing endpoint resolution.
- **DNS validation:** Test with a real domain whose CNAME can be programmatically controlled. Use a subdomain of a domain you own (e.g., `test-{guid}.sandbox.short.io`).
- **Quota monitoring:** CloudWatch alarm when distribution tenant count exceeds 8,000.

---

## Summary for Implementation

| Aspect | Decision |
|---|---|
| **IaC tool** | Terraform (`aws` provider v6.28.0+) — stable, well-tested, matches existing repo patterns |
| **Certificate model** | CloudFront-managed certificates (`ValidationTokenHost: "cloudfront"`) for new domains |
| **DNS model** | Subdomain CNAME only for initial release |
| **Zone apex** | Deferred — track AWS fix, implement apex redirect service if demand requires |
| **Account sharding** | Architecture ready but not implemented until approaching 8,000 tenants |
| **Provisioning service** | C# service in `src/ControlPlane/` calling `Amazon.CloudFront` SDK |
| **Status polling** | Hosted background service (`IHostedService`) in ControlPlane API |
| **Monitoring** | CloudWatch alarm at 80% quota + distribution tenant status metrics |

---

## References

- [ADR-001: CloudFront SaaS Manager for Domain Management](../decisions/ADR-001-cloudfront-saas-manager.md)
- [AWS CloudFront API Reference (PDF)](https://docs.aws.amazon.com/cloudfront/latest/APIReference/cloudfront-api.pdf)
- [Terraform aws_cloudfront_distribution_tenant](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/cloudfront_distribution_tenant)
- [CloudFront SaaS Manager Feature Page](https://aws.amazon.com/cloudfront/features/saas-manager/)
- [Zone Apex / ALIAS Issue on AWS re:Post](https://repost.aws/questions/QU6jYSBETpTF6sKyBZsK5L8A)
- [Terraform Provider Issue #46302](https://github.com/hashicorp/terraform-provider-aws/issues/46302)
