# API Reference

Complete reference for all Short.io API endpoints.

| Resource | Base path | Permissions needed |
|----------|-----------|-------------------|
| [Links](links.md) | `/api/v1/tenants/{tenantId}/links` | `link:read`, `link:write` |
| [Domains](domains.md) | `/api/v1/tenants/{tenantId}/domains` | `domain:read`, `domain:write` |
| [API Keys](api-keys.md) | `/api/v1/tenants/{tenantId}/api-keys` | `apikey:manage` |
| [Plans](plans.md) | `/api/v1/plans` | None (public) |
| [Invoices](invoices.md) | `/api/v1/tenants/{tenantId}/invoices` | `billing:read` |
| [Tenant settings](tenants.md) | `/api/v1/tenants/{tenantId}` | `tenant:read`, `tenant:write` |

## Common patterns

### Pagination

List endpoints use two styles of pagination:

**Cursor-based** (Links): Use the `nextCursor` from the response as the `cursor` query parameter on the next request.

```bash
GET /links?limit=20
→ { "items": [...], "nextCursor": "eyJ..." }

GET /links?limit=20&cursor=eyJ...
→ { "items": [...], "nextCursor": null }  # last page
```

**Page-based** (Domains, Tenants, Invoices): Use `page` and `pageSize` parameters.

```bash
GET /domains?page=1&pageSize=20
→ { "items": [...], "page": 1, "pageSize": 20, "totalCount": 47 }
```

### Filtering

Most list endpoints accept a `status` query parameter to filter by resource status. Check the specific reference page for available filter values.

### Partial updates (PATCH)

`PATCH` endpoints only change fields you explicitly include in the request body. Omitted fields are unchanged. Include only the fields you want to modify.

### Soft deletes

Links and domains are soft-deleted by default: they stop serving traffic but their data is retained for 30 days. Soft-deleted resources can be restored within the retention window.

To permanently delete a link immediately, pass `?permanent=true` on the delete request.

## Health endpoints

These endpoints are unversioned and require no authentication:

| Endpoint | Description |
|----------|-------------|
| `GET /health` | Returns `200 OK` when the API is up |
| `GET /health/details` | Returns dependency health (database connectivity) |
