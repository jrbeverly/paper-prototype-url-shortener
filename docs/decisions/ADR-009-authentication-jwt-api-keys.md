# ADR-009: Authentication Scheme (JWT + API Keys)

**Status:** Accepted
**Date:** 2026-05-30
**Deciders:** Solo founder

---

## Purpose

Choose how users and programmatic clients authenticate to the platform's APIs — covering both browser-based dashboard access and machine-to-machine API access.

## Context

The platform has two distinct client types with different authentication needs:

1. **Browser users** (dashboard SPA): Log in with email/password or SSO, manage links and domains through the UI. Need sessions, role-based access, and secure token storage in the browser.
2. **API clients** (developer scripts, CI/CD, SDKs): Create and manage links programmatically. Need long-lived, revocable credentials that don't require interactive login. May be used from server environments.

These two client types have conflicting requirements: browser users need short-lived tokens with refresh capability; API clients need stable credentials that don't require re-authentication flows.

Authentication scheme options evaluated:

| Scheme | Browser | API | Pros | Cons |
|---|---|---|---|---|
| **JWT only (Cognito)** | Works | Cumbersome | Single auth system | API clients need client credentials flow; token refresh adds complexity for scripts |
| **API Keys only** | Doesn't work | Works | Simple for machines | No user sessions, no RBAC, no audit trail per user |
| **JWT + API Keys** | Works | Works | Each client type gets its optimal flow | Two auth systems to operate |
| **OAuth 2.0 full flow** | Works | Works | Industry standard | Heavy for a solo operator; authorization server complexity |

## Approach

Use a **dual authentication scheme**: **JWT (via AWS Cognito)** for browser users and **API Keys** for programmatic clients. Both are enforced at the API Gateway and application middleware layers, not in individual endpoints.

### Browser Authentication (JWT)

1. User logs in via Cognito Hosted UI or the SPA's login form (using Amazon Cognito Identity Provider SDK).
2. On successful authentication, Cognito returns JWT tokens: an **Access Token** (1 hour), an **ID Token** (1 hour), and a **Refresh Token** (30 days).
3. The SPA stores tokens in memory (Access Token) and in a secure, HttpOnly cookie (Refresh Token) via the BFF (Backend For Frontend) pattern.
4. Every API request includes the Access Token in the `Authorization: Bearer <token>` header.
5. API Gateway validates the JWT signature against Cognito's JWKS endpoint. Validated claims (sub, email, cognito:groups) are forwarded to Lambda in the request context.

### API Key Authentication

1. User generates an API key from the dashboard (Management API endpoint: `POST /api/v1/workspaces/{id}/api-keys`).
2. The API key is a cryptographically random 256-bit value, stored as a SHA-256 hash in the control plane Postgres database. The raw key is shown once at creation.
3. API requests include the key in the `X-Api-Key` header or as a Bearer token.
4. A custom Lambda authorizer (or API Gateway API key feature for simpler cases) validates the key hash, resolves the associated tenant/user, and injects tenant context into the request.
5. API keys have configurable permissions (read-only, read-write, admin) and can be scoped to specific workspaces.

### BFF Pattern for SPA Token Management

To avoid storing refresh tokens in the browser (where they are vulnerable to XSS), the SPA uses a Backend For Frontend (BFF) proxy:

```
Browser (SPA)                     BFF (Lambda)                    Cognito
    │                                 │                               │
    │ POST /auth/login                 │                               │
    │ {email, password}               │                               │
    │ ─────────────────────────────→  │  InitiateAuth                 │
    │                                 │ ─────────────────────────────→│
    │                                 │  ←── Access + Refresh tokens  │
    │  Set-Cookie: refresh_token      │                               │
    │  (HttpOnly, Secure, SameSite)   │                               │
    │ ←───────────────────────────── │                               │
    │                                 │                               │
    │ GET /api/v1/links               │                               │
    │ Authorization: Bearer <access>  │                               │
    │ ──────────────────────────────→ │                               │
    │                                 │                               │
    │ (token expired)                 │                               │
    │ POST /auth/refresh              │                               │
    │ Cookie: refresh_token           │                               │
    │ ──────────────────────────────→ │  InitiateAuth (REFRESH_TOKEN) │
    │                                 │ ─────────────────────────────→│
    │  ←── new Access Token          │  ←── new tokens               │
```

## Constraints

- **Cognito limits:** Cognito User Pools have a default limit of 50 app integrations per user pool. This is sufficient for one SPA + one BFF per environment.
- **API key storage:** Raw API keys must never be logged, stored in plaintext, or returned after creation. Only the SHA-256 hash is stored in Postgres. Key generation uses `crypto.randomUUID()` or equivalent.
- **Token size:** Cognito JWTs can exceed 4 KB with many group claims. API Gateway has a 10 KB header limit — keep group claims minimal. For large permission sets, use a separate permission lookup rather than embedding all permissions in the JWT.
- **Refresh token rotation:** Cognito supports refresh token rotation. Enable it to reduce the window of compromise if a refresh token is leaked.
- **BFF adds latency:** The BFF adds one hop for token refresh. This is acceptable because token refresh is infrequent (once per hour) and not on the critical redirect path.

## Decisions

1. **JWT + API Keys over JWT-only** — Forcing API clients through the Cognito client credentials flow adds friction (client ID, client secret, token endpoint) that is unnecessary for simple link management scripts. API keys are the de facto standard for developer APIs (Stripe, GitHub, AWS).

2. **AWS Cognito over custom auth** — Cognito handles user registration, email verification, password reset, MFA, and token issuance. Building this from scratch is high-risk (security) and high-effort (password hashing, email delivery, token lifecycle). Cognito's free tier (50,000 MAU) covers the MVP and beyond.

3. **Cognito over Auth0/Okta** — Minimizes third-party SaaS dependencies. Cognito is AWS-native with IAM integration, unified billing, and no additional SLA to manage. See [ADR-003](ADR-003-serverless-first-architecture.md).

4. **BFF pattern over implicit flow** — Storing refresh tokens in browser JavaScript is vulnerable to XSS. The BFF stores the refresh token in an HttpOnly, Secure, SameSite cookie that JavaScript cannot read. This is the current best practice for SPAs (per IETF OAuth 2.0 BCP).

5. **API keys as SHA-256 hashes over HMAC** — HMAC signing (like AWS SigV4) is more secure but significantly harder for developers to implement correctly. API keys as bearer tokens in a header are simple, widely understood, and sufficient when transmitted over HTTPS. HMAC signing can be added later as an option for enterprise customers.

6. **Policy services for authorization over inline checks** — Authorization logic (RBAC, tenant scoping) is centralized in policy service classes (`IAuthContextPolicy`, `ILibraryPolicy`) that throw domain exceptions on failure. Individual endpoints call policy services, not inline permission checks. See [.claude/csharp/minimal-api.md](../../.claude/csharp/minimal-api.md#policy-services-pattern) for the full pattern.

## Trade-offs

| Trade-off | Detail |
|---|---|
| **Two auth systems** | JWT + API Keys means two validation paths, two revocation mechanisms, and two sets of operational concerns. Acceptable because the client types and their requirements are genuinely different — a single system would compromise one client type or the other. |
| **Cognito lock-in** | Migrating off Cognito means migrating user accounts and password hashes. Cognito supports user export (CSV) and the OAuth 2.0/OIDC standards are portable. Migration cost is medium but unlikely to be needed — Cognito is a mature, widely-used service. |
| **BFF complexity** | The BFF adds a Lambda function and cookie management that wouldn't exist with a pure SPA + token-in-memory approach. Acceptable because the security benefit (XSS resistance) outweighs the operational cost for a platform that handles customer domains and billing. |
| **API key plaintext exposure** | API keys as bearer tokens could be logged accidentally. Mitigation: API Gateway strips the `X-Api-Key` header from CloudWatch logs; Postgres only stores the hash; key generation code paths never log the raw value. |

## Evolution

- If enterprise customers require SAML/OIDC SSO, Cognito User Pools support federated identity providers. Add a Cognito federated identity provider per enterprise customer.
- If API key security becomes a concern (e.g., enterprise customers with compliance requirements), add HMAC-signed requests (AWS SigV4-style) as an alternative to bearer API keys.
- If the Cognito free tier is exceeded (50,000+ MAU), the per-MAU pricing is modest (~$0.0055/MAU). The cost is passed through in plan pricing.
- If the BFF cookie-based refresh becomes a bottleneck, add a token endpoint that returns the access token directly (for native mobile apps, if ever needed).

## Related ADRs

- [ADR-003: Serverless-First Architecture](ADR-003-serverless-first-architecture.md) — Cognito and API Gateway authorizers are serverless, aligning with zero-cost-when-idle.
- [ADR-004: C# for Backend Services](ADR-004-csharp-backend.md) — BFF and auth middleware are implemented in C# Minimal API.
- [ADR-008: API Versioning Strategy](ADR-008-api-versioning-strategy.md) — Auth applies across API versions through middleware.
- [ADR-012: Error Handling and RFC 7807](ADR-012-error-handling-rfc-7807.md) — Auth failures (401, 403) use Problem Details error format.
