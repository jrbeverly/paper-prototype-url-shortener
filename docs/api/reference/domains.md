# Domains

Custom branded domains let you serve short links from your own domain (e.g., `go.yourbrand.com`) instead of a generic Short.io subdomain. Each domain must be verified via DNS before links can be created on it.

**Base path:** `/api/v1/tenants/{tenantId}/domains`

**Required permissions:** `domain:read` (read operations), `domain:write` (create/update/delete/verify)

---

## The domain object

```json
{
  "id": "d1e2f3a4-b5c6-7890-abcd-ef1234567890",
  "tenantId": "a0b1c2d3-e4f5-6789-abcd-ef0123456789",
  "hostname": "go.yourbrand.com",
  "status": "active",
  "certificateStatus": "issued",
  "linkCount": 47,
  "settings": {
    "defaultRedirectUrl": "https://yourbrand.com",
    "errorPageBranding": null,
    "notFoundBehavior": "404"
  },
  "createdAt": "2024-10-15T10:00:00Z",
  "updatedAt": "2024-10-16T08:30:00Z",
  "deletedAt": null
}
```

| Field | Type | Description |
|-------|------|-------------|
| `id` | uuid | Unique identifier |
| `tenantId` | uuid | Workspace this domain belongs to |
| `hostname` | string | The domain name (e.g., `go.yourbrand.com`) |
| `status` | string | Domain lifecycle status (see below) |
| `certificateStatus` | string | TLS certificate status: `pending`, `issued`, or `failed` |
| `linkCount` | integer | Number of active links on this domain |
| `settings.defaultRedirectUrl` | string \| null | Where the domain root redirects (`https://go.yourbrand.com/` → here) |
| `settings.errorPageBranding` | string \| null | Custom text shown on error pages |
| `settings.notFoundBehavior` | string | What happens when a slug is not found: `404`, `redirect` (to `defaultRedirectUrl`), or `passthrough` |
| `createdAt` | datetime | Registration timestamp (UTC) |
| `updatedAt` | datetime \| null | Last update timestamp (UTC) |
| `deletedAt` | datetime \| null | Soft-deletion timestamp (UTC) |

### Domain statuses

| Status | Description |
|--------|-------------|
| `pending_verification` | Domain registered. DNS records must be added. |
| `verifying` | Verification check is in progress. |
| `verification_failed` | DNS check failed. Check that your records are configured correctly. |
| `active` | Verified and serving links. |

---

## Register a domain

```
POST /api/v1/tenants/{tenantId}/domains
```

**Required permission:** `domain:write`

### Request body

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `hostname` | string | Yes | Domain name without protocol. Max 253 characters. e.g. `go.yourbrand.com` |

### Example

```bash
curl -X POST https://api.short.io/api/v1/tenants/{tenantId}/domains \
  -H "X-API-Key: sk_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{"hostname": "go.yourbrand.com"}'
```

### Response

`201 Created`

```json
{
  "id": "d1e2f3a4-b5c6-7890-abcd-ef1234567890",
  "hostname": "go.yourbrand.com",
  "status": "pending_verification",
  "verificationInstructions": {
    "txtName": "_short-io-verify.go.yourbrand.com",
    "txtValue": "short-io-verify=xK9mP2qR",
    "cnameName": "go.yourbrand.com",
    "cnameValue": "cname.short.io"
  },
  "createdAt": "2024-11-01T14:32:00Z"
}
```

The `verificationInstructions` object contains the DNS records you must create at your DNS provider before calling [verify](#verify-a-domain).

### Errors

| Status | Cause |
|--------|-------|
| `400` | Invalid hostname format |
| `409` | Domain is already registered (by you or another workspace) |
| `422` | Plan domain limit reached |

---

## DNS setup

After registering a domain, add both DNS records at your DNS provider. The records are unique to your workspace.

| Record type | Name | Value |
|------------|------|-------|
| **TXT** | `_short-io-verify.go.yourbrand.com` | `short-io-verify=xK9mP2qR` |
| **CNAME** | `go.yourbrand.com` | `cname.short.io` |

> **Propagation time:** DNS changes can take anywhere from a few minutes to 48 hours to propagate globally. Wait at least 5 minutes after adding the records before attempting verification.

Once the records are in place, call the [verify endpoint](#verify-a-domain) to activate the domain.

---

## Verify a domain

```
POST /api/v1/tenants/{tenantId}/domains/{domainId}/verify
```

**Required permission:** `domain:write`

Triggers a live DNS lookup to check that both the TXT and CNAME records are correctly configured. If verification succeeds, the domain status changes to `active`.

**Rate limit:** 1 attempt per domain per 60 seconds.

### Example

```bash
curl -X POST https://api.short.io/api/v1/tenants/{tenantId}/domains/{domainId}/verify \
  -H "X-API-Key: sk_your_key_here"
```

### Response

`200 OK`

```json
{
  "id": "d1e2f3a4-...",
  "hostname": "go.yourbrand.com",
  "status": "active",
  "txtCheck": {
    "passed": true,
    "expected": "short-io-verify=xK9mP2qR",
    "actual": "short-io-verify=xK9mP2qR",
    "error": null
  },
  "cnameCheck": {
    "passed": true,
    "expected": "cname.short.io",
    "actual": "cname.short.io",
    "error": null
  },
  "message": null,
  "checkedAt": "2024-11-01T14:35:00Z"
}
```

If verification fails, `status` remains `verification_failed` and the check objects describe what went wrong:

```json
{
  "status": "verification_failed",
  "txtCheck": {
    "passed": false,
    "expected": "short-io-verify=xK9mP2qR",
    "actual": null,
    "error": "TXT record not found"
  }
}
```

### Errors

| Status | Cause |
|--------|-------|
| `404` | Domain not found |
| `429` | Verification rate limit exceeded (wait `retryAfter` seconds) |

---

## List domains

```
GET /api/v1/tenants/{tenantId}/domains
```

**Required permission:** `domain:read`

### Query parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `status` | string | — | Filter by status: `pending_verification`, `verifying`, `verification_failed`, `active` |
| `page` | integer | `1` | Page number |
| `pageSize` | integer | `20` | Items per page. Maximum `100`. |

### Example

```bash
curl "https://api.short.io/api/v1/tenants/{tenantId}/domains?status=active" \
  -H "X-API-Key: sk_your_key_here"
```

### Response

```json
{
  "items": [
    {
      "id": "d1e2f3a4-...",
      "hostname": "go.yourbrand.com",
      "status": "active",
      "certificateStatus": "issued",
      "linkCount": 47,
      "createdAt": "2024-10-15T10:00:00Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 3
}
```

---

## Get a domain

```
GET /api/v1/tenants/{tenantId}/domains/{domainId}
```

**Required permission:** `domain:read`

### Example

```bash
curl https://api.short.io/api/v1/tenants/{tenantId}/domains/{domainId} \
  -H "X-API-Key: sk_your_key_here"
```

### Response

`200 OK` with the full [domain object](#the-domain-object) including `settings`.

---

## Update a domain

```
PATCH /api/v1/tenants/{tenantId}/domains/{domainId}
```

**Required permission:** `domain:write`

### Request body

| Field | Type | Description |
|-------|------|-------------|
| `defaultRedirectUrl` | string | Where the domain root redirects. Absolute URL. |
| `errorPageBranding` | string | Custom text shown on 404 error pages |
| `notFoundBehavior` | string | `404` (return 404), `redirect` (redirect to `defaultRedirectUrl`), or `passthrough` (pass request upstream) |

### Example

```bash
curl -X PATCH https://api.short.io/api/v1/tenants/{tenantId}/domains/{domainId} \
  -H "X-API-Key: sk_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{
    "defaultRedirectUrl": "https://yourbrand.com",
    "notFoundBehavior": "redirect"
  }'
```

### Response

`200 OK` with the updated [domain object](#the-domain-object).

---

## Delete a domain

```
DELETE /api/v1/tenants/{tenantId}/domains/{domainId}
```

**Required permission:** `domain:write`

Deletes the domain and disables all links on it. This is a soft delete.

### Example

```bash
curl -X DELETE https://api.short.io/api/v1/tenants/{tenantId}/domains/{domainId} \
  -H "X-API-Key: sk_your_key_here"
```

### Response

`204 No Content`
