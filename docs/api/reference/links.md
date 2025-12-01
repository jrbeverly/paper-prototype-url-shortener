# Links

Short links are the core resource of the Short.io API. Each link maps a slug on a custom domain to a destination URL.

**Base path:** `/api/v1/tenants/{tenantId}/links`

**Required permissions:** `link:read` (read operations), `link:write` (create/update/delete)

---

## The link object

```json
{
  "id": "3f2e1d0c-b4a5-6789-cdef-012345678901",
  "domainId": "d1e2f3a4-b5c6-7890-abcd-ef1234567890",
  "destinationUrl": "https://example.com/long-path",
  "slug": "summer-sale",
  "shortUrl": "https://go.yourbrand.com/summer-sale",
  "redirectType": "302",
  "status": "active",
  "clickCount": 142,
  "expiresAt": null,
  "rules": null,
  "createdAt": "2024-11-01T14:32:00Z",
  "updatedAt": "2024-11-02T09:15:00Z",
  "deletedAt": null,
  "version": 2
}
```

| Field | Type | Description |
|-------|------|-------------|
| `id` | uuid | Unique identifier |
| `domainId` | uuid | ID of the domain this link belongs to |
| `destinationUrl` | string | The URL this link redirects to |
| `slug` | string | The path component of the short URL |
| `shortUrl` | string | The full short URL (`https://{domain}/{slug}`) |
| `redirectType` | string | HTTP redirect code: `301`, `302`, `307`, or `308` |
| `status` | string | `active`, `paused`, or `deleted` |
| `clickCount` | integer | Total number of clicks recorded |
| `expiresAt` | datetime \| null | When the link expires (stops redirecting) |
| `rules` | object \| null | Key-value routing rules |
| `createdAt` | datetime | Creation timestamp (UTC) |
| `updatedAt` | datetime \| null | Last modification timestamp (UTC) |
| `deletedAt` | datetime \| null | Soft-deletion timestamp (UTC). Non-null for deleted links. |
| `version` | integer | Incremented on each update. Used for cache invalidation. |

### Link statuses

| Status | Description |
|--------|-------------|
| `active` | Link is live and redirecting |
| `paused` | Link is temporarily disabled (returns 404) |
| `deleted` | Soft-deleted. Not served. Recoverable for 30 days. |

### Redirect types

| Type | Behavior | Use when |
|------|----------|----------|
| `302` (default) | Temporary redirect. Browsers and crawlers re-fetch on each visit. | Campaign links, A/B tests, anything that might change |
| `301` | Permanent redirect. Browsers and crawlers cache indefinitely. | Canonical, permanent URLs |
| `307` | Temporary redirect, preserves HTTP method (POST stays POST) | Forms, API proxies |
| `308` | Permanent redirect, preserves HTTP method | Permanent API proxies |

> **Warning:** `301` and `308` redirects are cached by browsers. Once a visitor has seen the redirect, changing the destination URL in the Short.io API will not affect them until their browser cache expires (which can take months). Use `302` for any link you might want to update.

---

## Create a link

```
POST /api/v1/tenants/{tenantId}/links
```

**Required permission:** `link:write`

### Request body

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `domainId` | uuid | Yes | ID of the domain to create the link on |
| `destinationUrl` | string | Yes | Absolute HTTP or HTTPS URL |
| `slug` | string | No | Custom slug. Auto-generated (6-8 chars) if omitted. Alphanumeric and hyphens only, max 100 chars. |
| `redirectType` | string | No | `301`, `302` (default), `307`, or `308` |
| `expiresAt` | datetime | No | Expiry timestamp. Must be in the future. Omit for non-expiring links. |

### Example

```bash
curl -X POST https://api.short.io/api/v1/tenants/{tenantId}/links \
  -H "X-API-Key: sk_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{
    "domainId": "d1e2f3a4-b5c6-7890-abcd-ef1234567890",
    "destinationUrl": "https://example.com/products/summer-collection?utm_source=email",
    "slug": "summer-sale",
    "redirectType": "302",
    "expiresAt": "2024-12-31T23:59:59Z"
  }'
```

```python
response = requests.post(
    f"{BASE_URL}/links",
    headers={"X-API-Key": API_KEY},
    json={
        "domainId": "d1e2f3a4-...",
        "destinationUrl": "https://example.com/products/summer-collection",
        "slug": "summer-sale",
    },
)
link = response.json()
print(link["shortUrl"])  # https://go.yourbrand.com/summer-sale
```

```javascript
const link = await fetch(`${BASE_URL}/links`, {
  method: "POST",
  headers: { "X-API-Key": API_KEY, "Content-Type": "application/json" },
  body: JSON.stringify({
    domainId: "d1e2f3a4-...",
    destinationUrl: "https://example.com/products/summer-collection",
    slug: "summer-sale",
  }),
}).then(r => r.json());

console.log(link.shortUrl); // https://go.yourbrand.com/summer-sale
```

### Response

`201 Created` with the [link object](#the-link-object).

### Errors

| Status | Cause |
|--------|-------|
| `400` | Invalid destination URL, invalid slug format, invalid redirect type, expiry date in the past |
| `404` | Domain not found or not active |
| `409` | A link with this slug already exists on the domain |
| `422` | Plan link limit reached on this domain |

---

## List links

```
GET /api/v1/tenants/{tenantId}/links
```

**Required permission:** `link:read`

Results are returned in descending `createdAt` order by default.

### Query parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `cursor` | string | — | Pagination cursor from the previous response's `nextCursor` field |
| `limit` | integer | `20` | Items per page. Maximum `100`. |
| `search` | string | — | Full-text search across slug and destination URL |
| `domainId` | uuid | — | Filter to a specific domain |
| `status` | string | — | Filter by status: `active`, `paused`, or `deleted` |
| `sort` | string | `created_at` | Sort field. Options: `created_at`, `click_count` |

### Example

```bash
# List first 20 active links
curl "https://api.short.io/api/v1/tenants/{tenantId}/links?status=active&limit=20" \
  -H "X-API-Key: sk_your_key_here"

# Get next page using cursor
curl "https://api.short.io/api/v1/tenants/{tenantId}/links?cursor=eyJpZCI6Ii4uLiJ9" \
  -H "X-API-Key: sk_your_key_here"
```

### Response

```json
{
  "items": [ ...link objects... ],
  "nextCursor": "eyJpZCI6IjNmMmUxZDBjLi4uIn0",
  "totalCount": 842
}
```

`nextCursor` is `null` when there are no more pages.

---

## Get a link

```
GET /api/v1/tenants/{tenantId}/links/{linkId}
```

**Required permission:** `link:read`

### Example

```bash
curl https://api.short.io/api/v1/tenants/{tenantId}/links/{linkId} \
  -H "X-API-Key: sk_your_key_here"
```

### Response

`200 OK` with the [link object](#the-link-object) including `rules` and `analytics`.

---

## Update a link

```
PATCH /api/v1/tenants/{tenantId}/links/{linkId}
```

**Required permission:** `link:write`

Only include the fields you want to change. Omitted fields are left unchanged.

### Request body

| Field | Type | Description |
|-------|------|-------------|
| `destinationUrl` | string | New destination URL |
| `redirectType` | string | `301`, `302`, `307`, or `308` |
| `expiresAt` | datetime | New expiry date. Must be in the future. |
| `clearExpiresAt` | boolean | Set to `true` to remove expiry (make the link non-expiring) |
| `status` | string | `active` or `paused` |
| `rules` | object | New routing rules (key-value pairs) |
| `clearRules` | boolean | Set to `true` to remove all routing rules |

### Example

```bash
# Pause a link
curl -X PATCH https://api.short.io/api/v1/tenants/{tenantId}/links/{linkId} \
  -H "X-API-Key: sk_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{"status": "paused"}'

# Change destination URL and remove expiry
curl -X PATCH https://api.short.io/api/v1/tenants/{tenantId}/links/{linkId} \
  -H "X-API-Key: sk_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{"destinationUrl": "https://example.com/new-page", "clearExpiresAt": true}'
```

```python
# Change destination URL
response = requests.patch(
    f"{BASE_URL}/links/{link_id}",
    headers={"X-API-Key": API_KEY},
    json={"destinationUrl": "https://example.com/new-page"},
)
```

### Response

`200 OK` with the updated [link object](#the-link-object).

> **Note:** `version` is incremented on every successful update. The redirect service uses `version` to invalidate its cache.

---

## Delete a link

```
DELETE /api/v1/tenants/{tenantId}/links/{linkId}
```

**Required permission:** `link:write`

By default, this is a **soft delete**: the link stops serving traffic but can be [restored](#restore-a-link) within 30 days.

### Query parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `permanent` | boolean | `false` | Set to `true` to hard-delete immediately and permanently |

### Example

```bash
# Soft delete (recoverable)
curl -X DELETE "https://api.short.io/api/v1/tenants/{tenantId}/links/{linkId}" \
  -H "X-API-Key: sk_your_key_here"

# Permanent delete (irreversible)
curl -X DELETE "https://api.short.io/api/v1/tenants/{tenantId}/links/{linkId}?permanent=true" \
  -H "X-API-Key: sk_your_key_here"
```

### Response

`204 No Content`

---

## Restore a link

```
POST /api/v1/tenants/{tenantId}/links/{linkId}/restore
```

**Required permission:** `link:write`

Restore a soft-deleted link. The link returns to `active` status and begins serving traffic again.

Links can only be restored within **30 days** of deletion.

### Example

```bash
curl -X POST https://api.short.io/api/v1/tenants/{tenantId}/links/{linkId}/restore \
  -H "X-API-Key: sk_your_key_here"
```

### Response

`200 OK` with the restored [link object](#the-link-object).

### Errors

| Status | Cause |
|--------|-------|
| `400` | Link was permanently deleted, or the 30-day restore window has passed |
| `404` | Link not found |

---

## Bulk create

```
POST /api/v1/tenants/{tenantId}/links/bulk
```

**Required permission:** `link:write`

Create up to 10,000 links in a single request. Individual failures do not abort the batch — results are returned per-link.

### Request body

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `links` | array | Yes | Array of [create link](#create-a-link) request objects |
| `processAsync` | boolean | No | For requests with >1,000 links, set `true` to process in the background and receive a `202 Accepted` with a `jobId` |

### Example

```bash
curl -X POST https://api.short.io/api/v1/tenants/{tenantId}/links/bulk \
  -H "X-API-Key: sk_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{
    "links": [
      {"domainId": "d1e2f3a4-...", "destinationUrl": "https://example.com/page1", "slug": "p1"},
      {"domainId": "d1e2f3a4-...", "destinationUrl": "https://example.com/page2", "slug": "p2"},
      {"domainId": "d1e2f3a4-...", "destinationUrl": "https://example.com/page3"}
    ]
  }'
```

### Response

```json
{
  "totalRequested": 3,
  "succeeded": 2,
  "failed": 1,
  "results": [
    { "index": 0, "success": true, "link": { ...link object... } },
    { "index": 1, "success": false, "link": null, "error": "Slug 'p2' already exists on this domain." },
    { "index": 2, "success": true, "link": { ...link object... } }
  ]
}
```

For async processing (`processAsync: true` with >1,000 links), the response is `202 Accepted`:

```json
{ "jobId": "job_abc123..." }
```

---

## Import CSV

```
POST /api/v1/tenants/{tenantId}/links/import
Content-Type: multipart/form-data
```

**Required permission:** `link:write`

Upload a CSV file to create multiple links. The CSV must follow this format:

```csv
domain_id,slug,destination_url,redirect_type,expires_at
d1e2f3a4-...,summer-sale,https://example.com/sale,302,
d1e2f3a4-...,promo,https://example.com/promo,302,2024-12-31T23:59:59Z
```

The `slug`, `redirect_type`, and `expires_at` columns are optional and follow the same rules as the [create link](#create-a-link) endpoint.

### Example

```bash
curl -X POST https://api.short.io/api/v1/tenants/{tenantId}/links/import \
  -H "X-API-Key: sk_your_key_here" \
  -F "file=@links.csv"
```

### Response

Same format as [bulk create](#bulk-create).

---

## Export CSV

```
GET /api/v1/tenants/{tenantId}/links/export
```

**Required permission:** `link:read`

Download all links (including deleted) as a CSV file.

```
Content-Type: text/csv
Content-Disposition: attachment; filename="links-export.csv"
```

### Example

```bash
curl https://api.short.io/api/v1/tenants/{tenantId}/links/export \
  -H "X-API-Key: sk_your_key_here" \
  --output links-export.csv
```

The exported CSV includes columns:

```csv
id,domain_id,domain,slug,destination_url,short_url,redirect_type,expires_at,created_at
```
