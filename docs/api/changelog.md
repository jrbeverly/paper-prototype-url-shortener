# API Changelog

All notable changes to the Short.io API are documented here. The API follows [Semantic Versioning](https://semver.org/). Breaking changes are noted with **[Breaking]**.

---

## v1.0.0 — 2024-11-01

Initial public release of the Short.io API v1.

### Resources

**Links**
- `POST /api/v1/tenants/{tenantId}/links` — Create a short link with auto-generated or custom slug
- `GET /api/v1/tenants/{tenantId}/links` — List links with cursor-based pagination, search, and filtering
- `GET /api/v1/tenants/{tenantId}/links/{linkId}` — Get link details and analytics summary
- `PATCH /api/v1/tenants/{tenantId}/links/{linkId}` — Update destination URL, redirect type, expiry, rules
- `DELETE /api/v1/tenants/{tenantId}/links/{linkId}` — Soft-delete (recoverable for 30 days) or permanent delete
- `POST /api/v1/tenants/{tenantId}/links/{linkId}/restore` — Restore a soft-deleted link
- `POST /api/v1/tenants/{tenantId}/links/bulk` — Bulk create up to 10,000 links in a single request
- `POST /api/v1/tenants/{tenantId}/links/import` — Import links from a CSV file
- `GET /api/v1/tenants/{tenantId}/links/export` — Export all links as a CSV file

**Domains**
- `POST /api/v1/tenants/{tenantId}/domains` — Register a custom branded domain
- `GET /api/v1/tenants/{tenantId}/domains` — List domains with status filtering
- `GET /api/v1/tenants/{tenantId}/domains/{domainId}` — Get domain details and certificate status
- `PATCH /api/v1/tenants/{tenantId}/domains/{domainId}` — Update domain settings (404 behavior, root redirect)
- `DELETE /api/v1/tenants/{tenantId}/domains/{domainId}` — Delete a domain
- `POST /api/v1/tenants/{tenantId}/domains/{domainId}/verify` — Trigger DNS verification (rate-limited: 1/60s per domain)

**API Keys**
- `POST /api/v1/tenants/{tenantId}/api-keys` — Create an API key with scoped permissions
- `GET /api/v1/tenants/{tenantId}/api-keys` — List all API keys (masked)
- `DELETE /api/v1/tenants/{tenantId}/api-keys/{keyId}` — Revoke an API key

**Plans**
- `GET /api/v1/plans` — List all available subscription plans with limits and features (public, no auth)

**Invoices**
- `GET /api/v1/tenants/{tenantId}/invoices` — List invoice history
- `GET /api/v1/tenants/{tenantId}/invoices/upcoming` — Get the next scheduled invoice
- `GET /api/v1/tenants/{tenantId}/invoices/{invoiceId}` — Get invoice details with line items
- `GET /api/v1/tenants/{tenantId}/invoices/{invoiceId}/pdf` — Download invoice PDF (redirect)

**Tenant settings**
- `GET /api/v1/tenants/{tenantId}` — Get workspace details, limits, and settings
- `PATCH /api/v1/tenants/{tenantId}` — Update workspace name, logo, and notification settings
- `GET /api/v1/tenants/{tenantId}/trial` — Get trial status and days remaining
- `PATCH /api/v1/tenants/{tenantId}/trial` — Extend trial period

**Health**
- `GET /health` — API liveness probe
- `GET /health/details` — API liveness probe with dependency status

### Authentication

- API key authentication via `X-API-Key` header (key prefix: `sk_`)
- JWT bearer token authentication via `Authorization: Bearer` header
- Granular permission model: `link:read`, `link:write`, `domain:read`, `domain:write`, `tenant:read`, `tenant:write`, `billing:read`, `apikey:manage`
- Role shortcuts: `owner`, `admin`, `member`, `viewer`

### Error handling

- All errors use [RFC 7807 Problem Details](https://www.rfc-editor.org/rfc/rfc7807) format
- Content type: `application/problem+json`
- Domain verification rate limiting: `429 Too Many Requests` with `retryAfter` field

### Subscription plans

Six plans available at launch: `free`, `starter` ($9/mo), `pro` ($29/mo), `team` ($79/mo), `business` ($249/mo), `enterprise` (custom).

New workspaces automatically start a 14-day Pro trial.

---

## Versioning policy

The API URL path encodes the major version (`/api/v1/`). A new major version is only released for breaking changes. Minor additions (new endpoints, new optional fields, new query parameters) are made within the existing version without a version bump.

**What constitutes a breaking change:**
- Removing an endpoint or field
- Changing a field's type or format
- Making a previously optional field required
- Changing authentication or authorization behavior

**What is not a breaking change:**
- Adding new optional fields to responses
- Adding new optional query parameters
- Adding new endpoints
- Adding new values to an enum (write defensive code that handles unknown values)

Deprecated endpoints will be flagged with a `Deprecation` header at least 90 days before removal.
