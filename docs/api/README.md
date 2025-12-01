# Short.io API Documentation

Welcome to the Short.io API. Use this API to create and manage short links, custom domains, and API keys programmatically within your workspace.

## Base URL

```
https://api.short.io/api/v1
```

All endpoints are versioned. The current API version is `v1`. The version is specified in the URL path, not via headers.

## Authentication

The API supports two authentication methods:

| Method | Header | Use case |
|--------|--------|----------|
| API key | `X-API-Key: sk_...` | Server-to-server integrations, scripts |
| JWT bearer | `Authorization: Bearer <token>` | Dashboard sessions, short-lived access |

For most integrations, use an API key. See the [Authentication guide](authentication.md) for details.

## Your Tenant ID

Every resource in the API is scoped to your **tenant workspace**. Your `tenantId` (a UUID) appears in the URL of every request:

```
/api/v1/tenants/{tenantId}/links
```

Your `tenantId` is returned when your account is created and is visible in the dashboard under **Settings → Workspace**.

## Quick Start

Create your first short link in under 5 minutes: [Quickstart guide →](quickstart.md)

## API Reference

| Resource | Description |
|----------|-------------|
| [Links](reference/links.md) | Create, update, and manage short links |
| [Domains](reference/domains.md) | Register and verify custom branded domains |
| [API Keys](reference/api-keys.md) | Create and revoke API credentials |
| [Plans](reference/plans.md) | View available subscription plans and features |
| [Invoices](reference/invoices.md) | Access billing history and upcoming charges |
| [Tenant settings](reference/tenants.md) | Manage workspace settings |

## Guides

- [Authentication](authentication.md) — Generating API keys, permissions model
- [Rate limiting](rate-limiting.md) — Limits, headers, back-off strategies
- [Error handling](errors.md) — Error format, status codes, common fixes
- [Webhooks](webhooks.md) — Event notifications *(coming soon)*

## Changelog

See the [API changelog](changelog.md) for a history of additions and breaking changes.

## Response format

All responses are JSON. Successful responses return the resource object or a list wrapper. Error responses follow [RFC 7807 Problem Details](errors.md).

```http
Content-Type: application/json
```

## Date format

All timestamps are ISO 8601 UTC:

```
2024-11-01T14:32:00Z
```

## Amounts

Monetary amounts (invoices) are in the smallest currency unit (cents for USD).
