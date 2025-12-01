# ADR-008: API Versioning Strategy

**Status:** Accepted
**Date:** 2026-05-30
**Deciders:** Solo founder

---

## Purpose

Choose how the platform's REST APIs will be versioned — the mechanism for evolving API contracts without breaking existing clients.

## Context

The platform exposes multiple APIs:
- **Management API:** CRUD for links, domains, workspaces (consumed by the SPA dashboard)
- **Public API:** Programmatic link management for developers (consumed by customer scripts, CI/CD, SDKs)
- **Redirect API:** Internal lookup API invoked by CloudFront (not publicly versioned)

These APIs will evolve. New fields will be added, response shapes will change, and occasionally breaking changes will be necessary. Without a versioning strategy, every change risks breaking clients.

API versioning options evaluated:

| Strategy | How it works | Pros | Cons |
|---|---|---|---|
| **URL path** | `/api/v1/links`, `/api/v2/links` | Simplest, visible in logs, easy routing | URL changes on version bump |
| **Header** | `Accept: application/vnd.api.v2+json` | Clean URLs, content negotiation | Harder to discover, invisible in logs |
| **Query param** | `/api/links?version=2` | Simple, easy to test | Pollutes query namespace, easy to forget |
| **Hostname** | `v2.api.example.com` | Complete isolation | DNS/provisioning overhead per version |

## Approach

Use **URL path prefix versioning** (`/api/v{major}/`) as the primary mechanism, with **custom media types** as a secondary content negotiation layer for clients that prefer it.

The API version number is the **major version only** (v1, v2, v3). Minor, non-breaking additions do not increment the version — they are additive within the same major version (new optional fields, new endpoints).

In code, versioning is enforced through **namespace-based organization**:

```
src/UrlShortener/UrlShortener.Api/Routes/Links/v1/LinkCreateRoute.cs
src/UrlShortener/UrlShortener.Api/Routes/Links/v2/LinkCreateRoute.cs
```

Each version lives in its own namespace (`Routes.Links.v1`, `Routes.Links.v2`), enabling multiple versions to coexist without conditional logic. Route groups wire versions to URL paths in `Program.cs`:

```csharp
var v1 = app.MapGroup("/api/v1");
var v2 = app.MapGroup("/api/v2");
LinkCreateV1Route.Registration.Map(v1.MapGroup("/links"));
LinkCreateV2Route.Registration.Map(v2.MapGroup("/links"));
```

See [.claude/csharp/minimal-api.md](../../.claude/csharp/minimal-api.md) for the full endpoint pattern.

## Constraints

- **API Gateway routing:** API Gateway HTTP API routes on URL paths. URL path versioning maps directly to API Gateway stages and routes without custom routing logic.
- **OpenAPI generation:** Each major version produces its own OpenAPI spec document (e.g., `openapi-v1.json`, `openapi-v2.json`) so that generated TypeScript clients are version-specific and unambiguous. See [ADR-005](ADR-005-vue-typescript-frontend.md).
- **CloudFront caching:** The redirect API is internal and not publicly versioned. It uses a separate, unversioned path prefix (`/_redirect/`) to avoid caching conflicts with versioned management APIs.
- **Deprecation window:** When a new major version is introduced, the previous version is deprecated but supported for a minimum of 6 months. Deprecation is communicated via the `Sunset` HTTP header and in API documentation.

## Decisions

1. **URL path versioning over header-based versioning** — URL paths are visible in logs, API Gateway routing, and CloudFront cache keys. Header-based versioning hides the version from these systems and makes debugging harder. For a solo-maintained platform, observability trumps URL aesthetic purity.

2. **Major-only versioning over major.minor** — Breaking changes trigger a new major version. Non-breaking additions (new optional fields, new endpoints, new enum values) are added to the existing major version. This keeps the version count low and avoids version proliferation (no v1.1, v1.2, v1.3).

3. **Namespace-based versioning in code over conditional logic** — Each version lives in its own namespace with a complete copy of the endpoint code. This avoids `if (version == 2)` branches that grow unboundedly and make it hard to remove old versions. When v1 is retired, the `Routes/Links/v1/` directory is deleted.

4. **Separate OpenAPI specs per version over a single merged spec** — Merged specs create ambiguity for code generators. Separate specs produce clean, version-specific TypeScript types.

5. **Internal APIs are not versioned** — The redirect lookup API is internal (called by CloudFront, not external clients). It uses a stable, unversioned contract. If the contract must change, it changes atomically with the CloudFront Function that calls it — no versioning overhead needed.

## Trade-offs

| Trade-off | Detail |
|---|---|
| **URL pollution** | `/api/v1/links` is less "clean" than `/api/links` with a header. In practice, this is the dominant pattern in public APIs (Stripe, GitHub, AWS) and is well-understood by developers. |
| **Code duplication** | Namespace-based versioning means v2 is a copy of v1, modified. This duplicates code but eliminates conditional complexity and makes old version removal trivial (delete the directory). Acceptable because breaking changes are infrequent. |
| **SPA coupling** | The SPA is deployed with a specific API version baked into its build. When a new API version is deployed, the SPA must be updated and redeployed. This is acceptable because the SPA and API are deployed together. |
| **Route group overhead** | Each major version adds a route group. At 2-3 concurrent versions, this is negligible. API Gateway HTTP API has no per-route cost overhead. |

## Evolution

- If a version needs to be deprecated, add a `Sunset` header to all responses and log warnings for deprecated-version usage. Remove the version after the deprecation window.
- If the number of concurrent versions grows beyond 3, add a `Deprecation` response header and automated client notification (email for API key users).
- If header-based versioning is needed for specific clients (e.g., SDKs that embed the version in an `Accept` header), add it as a secondary mechanism — the URL path version remains the canonical one.

## Related ADRs

- [ADR-004: C# for Backend Services](ADR-004-csharp-backend.md) — C# Minimal API with namespace-based versioning.
- [ADR-005: Vue 3 + TypeScript for Frontend](ADR-005-vue-typescript-frontend.md) — OpenAPI-generated types consume version-specific specs.
- [ADR-009: Authentication Scheme (JWT + API Keys)](ADR-009-authentication-jwt-api-keys.md) — Auth applies across API versions through middleware, not per-endpoint logic.
- [ADR-012: Error Handling and RFC 7807](ADR-012-error-handling-rfc-7807.md) — Error response format is consistent across API versions.
