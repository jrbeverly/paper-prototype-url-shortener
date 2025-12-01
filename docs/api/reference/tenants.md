# Tenant settings

Your tenant workspace holds all your links, domains, and API keys. These endpoints let you read and update workspace settings and check your trial status.

**Base path:** `/api/v1/tenants/{tenantId}`

**Required permissions:** `tenant:read` (read), `tenant:write` (update), `billing:read` (trial status)

---

## The tenant object

```json
{
  "id": "a0b1c2d3-e4f5-6789-abcd-ef0123456789",
  "name": "Acme Corp",
  "email": "admin@acme.com",
  "plan": "pro",
  "status": "trialing",
  "limits": {
    "maxDomains": 50,
    "maxLinksPerDomain": 10000,
    "maxTrackedClicksPerMonth": 100000,
    "analyticsRetentionDays": 180
  },
  "settings": {
    "logoUrl": "https://acme.com/logo.png",
    "defaultRedirectType": "302",
    "notificationsEnabled": true,
    "notificationEmail": "ops@acme.com"
  },
  "createdAt": "2024-11-01T14:32:00Z",
  "updatedAt": "2024-11-03T09:00:00Z",
  "trial": {
    "isOnTrial": true,
    "hasUsedTrial": true,
    "trialEndsAt": "2024-11-15T14:32:00Z",
    "daysRemaining": 10,
    "trialPlan": "pro"
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `id` | uuid | Workspace identifier |
| `name` | string | Workspace display name |
| `email` | string | Primary contact email |
| `plan` | string | Current subscription plan: `free`, `starter`, `pro`, `team`, `business`, `enterprise` |
| `status` | string | Workspace status (see below) |
| `limits` | object | Active limits based on current plan |
| `limits.maxDomains` | integer | Maximum number of custom domains |
| `limits.maxLinksPerDomain` | integer | Maximum links per domain |
| `limits.maxTrackedClicksPerMonth` | integer | Monthly click tracking limit |
| `limits.analyticsRetentionDays` | integer | Analytics data retention in days |
| `settings.logoUrl` | string \| null | Custom logo URL |
| `settings.defaultRedirectType` | string \| null | Default redirect type for new links |
| `settings.notificationsEnabled` | boolean | Whether email notifications are enabled |
| `settings.notificationEmail` | string \| null | Override email for notifications |
| `createdAt` | datetime | Workspace creation timestamp (UTC) |
| `updatedAt` | datetime \| null | Last update timestamp (UTC) |
| `trial` | object \| null | Trial information (non-null if workspace has ever trialed) |

### Workspace statuses

| Status | Description |
|--------|-------------|
| `active` | Normal operation |
| `trialing` | On a 14-day free trial |
| `suspended` | Suspended by an admin. Links serve a suspension notice. |
| `past_due` | Payment failed. A grace period applies before suspension. |
| `deleted` | Soft-deleted. Data retained for 30-day cooling-off period. |

---

## Get workspace details

```
GET /api/v1/tenants/{tenantId}
```

**Required permission:** `tenant:read`

### Example

```bash
curl https://api.short.io/api/v1/tenants/{tenantId} \
  -H "X-API-Key: sk_your_key_here"
```

```python
response = requests.get(
    f"https://api.short.io/api/v1/tenants/{tenant_id}",
    headers={"X-API-Key": API_KEY},
)
workspace = response.json()
print(f"Plan: {workspace['plan']}, Status: {workspace['status']}")
print(f"Domains remaining: {workspace['limits']['maxDomains']}")
```

### Response

`200 OK` with the [tenant object](#the-tenant-object).

---

## Update workspace settings

```
PATCH /api/v1/tenants/{tenantId}
```

**Required permission:** `tenant:write`

Only include the fields you want to change. Plan and status cannot be changed through this endpoint.

### Request body

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Workspace display name. 2–100 characters. |
| `logoUrl` | string | Custom logo URL. Must be HTTPS. |
| `defaultRedirectType` | string | Default redirect type for new links: `301`, `302`, `307`, or `308` |
| `notificationsEnabled` | boolean | Enable or disable email notifications |
| `notificationEmail` | string | Override email address for notifications |

### Example

```bash
curl -X PATCH https://api.short.io/api/v1/tenants/{tenantId} \
  -H "X-API-Key: sk_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Acme Corp (updated)",
    "notificationsEnabled": true,
    "notificationEmail": "alerts@acme.com"
  }'
```

### Response

`200 OK` with the updated [tenant object](#the-tenant-object).

### Errors

| Status | Cause |
|--------|-------|
| `400` | Invalid field value (e.g., invalid email format, name too short) |
| `409` | Cannot update a deleted workspace |

---

## Get trial status

```
GET /api/v1/tenants/{tenantId}/trial
```

**Required permission:** `billing:read`

### Example

```bash
curl https://api.short.io/api/v1/tenants/{tenantId}/trial \
  -H "X-API-Key: sk_your_key_here"
```

### Response

```json
{
  "isOnTrial": true,
  "hasUsedTrial": true,
  "trialEndsAt": "2024-11-15T14:32:00Z",
  "daysRemaining": 10,
  "trialPlan": "pro"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `isOnTrial` | boolean | Whether the workspace is currently on a trial |
| `hasUsedTrial` | boolean | Whether a trial has ever been activated |
| `trialEndsAt` | datetime \| null | When the trial expires (UTC) |
| `daysRemaining` | integer \| null | Days remaining in the trial |
| `trialPlan` | string \| null | Plan features active during the trial (`pro`) |

When `isOnTrial` is `false` and `hasUsedTrial` is `false`, the workspace is eligible for a new trial. When `hasUsedTrial` is `true`, the trial has already been used.
