# ADR-004: C# for Backend Services

**Status:** Accepted
**Date:** 2026-05-30
**Deciders:** Solo founder

---

## Purpose

Choose the primary backend language for the platform's APIs, Lambda functions, and business logic.

## Context

The platform requires:
- A redirect API (high-throughput, low-latency key-value lookups)
- A management API (CRUD operations, auth, permissions, validation)
- Background workers (certificate polling, abuse scanning, billing sync)
- CLI tools (admin operations, data migrations)

The solo founder has experience across multiple ecosystems. The language choice must optimize for long-term maintainability, correctness, and operational simplicity — not just speed of initial development.

Candidate languages evaluated:

| Language | Strengths | Weaknesses |
|---|---|---|
| **C# (.NET 8)** | Strong typing, excellent tooling, native Lambda/Graviton2 support, minimal API pattern, LINQ, source generators | Larger cold starts than interpreted languages, smaller serverless ecosystem than Node.js |
| **Node.js/TypeScript** | Fastest Lambda cold starts, largest serverless ecosystem, same language as frontend | Runtime type safety gaps, callback-era library baggage, single-threaded |
| **Go** | Fast compilation, small binaries, good Lambda support | Smaller package ecosystem for web/API work, verbose error handling, less expressive type system |
| **Python** | Largest ML/data ecosystem, fast prototyping | Poor Lambda cold starts, GIL limits concurrency, dynamic typing risks at scale |

## Approach

Use **C# 12 on .NET 8 LTS** for all backend services, deployed as Lambda functions targeting the `provided.al2023` (ARM64/Graviton2) runtime.

Apply the Minimal API pattern: one endpoint per file, static classes, records for DTOs, repository pattern for data access. See `.claude/csharp/minimal-api.md` for the detailed pattern.

## Constraints

- .NET Lambda cold starts are slower than Node.js (200ms-1.5s vs 50ms-200ms). Acceptable for management APIs; for the redirect hot path, use CloudFront edge caching or provisioned concurrency. See [ADR-001](ADR-001-cloudfront-saas-manager.md) and [ADR-003](ADR-003-serverless-first-architecture.md).
- .NET Lambda deployment packages are larger than Go or Node.js (20-50 MB trimmed). This increases deployment time but does not affect cold start latency meaningfully with the managed runtime.
- The .NET serverless ecosystem (NuGet packages for Lambda, DynamoDB, EventBridge) is mature but smaller than the Node.js ecosystem. Core AWS SDK support is first-class.

## Decisions

1. **C# over Node.js/TypeScript** — Strong static typing, discriminated unions (via OneOf or similar), and LINQ reduce runtime errors in business logic. The correctness guarantees matter more for a solo operator than the faster prototyping speed of Node.js.
2. **C# over Go** — C# has a richer standard library, more expressive generics, and the Minimal API pattern maps directly to Lambda handlers. Go's simplicity is a strength, but the trade-off in expressiveness and ecosystem breadth matters for a feature-rich product.
3. **C# over Python** — Python's dynamic typing and GIL make it a poor fit for a solo-maintained production system where correctness and concurrency matter.
4. **.NET 8 LTS over .NET 9** — LTS provides 3 years of support (through November 2026), stability, and guarantees no breaking changes. Adopt .NET 10 LTS when available.

## Trade-offs

| Trade-off | Detail |
|---|---|
| **Cold start latency** | C# Lambda cold starts are slower than Node.js and Go. Mitigated by: (a) the redirect path uses CloudFront edge caching, so origin Lambda cold starts only affect cache misses; (b) management API cold starts are tolerable for admin operations; (c) provisioned concurrency is available if needed. |
| **Hiring** | C# developers may be harder to find in the startup/serverless niche than Node.js developers. For a solo founder, this is not a near-term concern; the ecosystem and tooling compensate. |
| **Shared language with frontend** | Using TypeScript on both frontend and backend would allow sharing types and validation logic. This convenience is outweighed by C#'s stronger type system and runtime safety. API contracts are defined via OpenAPI specs and auto-generated types on both sides. |

## Evolution

- If cold starts become a bottleneck despite edge caching, evaluate Native AOT compilation (.NET 8+), which reduces cold start by pre-compiling to native code (trade-off: no reflection, larger deployment package).
- If specific workloads benefit from Node.js (e.g., CloudFront Functions which only support JavaScript), keep them narrowly scoped to the edge layer.
- Adopt .NET 10 LTS when available, targeting C# 14 features.

## Related ADRs

- [ADR-003: Serverless-First Architecture](ADR-003-serverless-first-architecture.md) — C# runs on Lambda with native Graviton2 support.
- [ADR-005: Vue 3 + TypeScript for Frontend](ADR-005-vue-typescript-frontend.md) — TypeScript on the frontend, C# on the backend.
- [ADR-006: Postgres for Control Plane](ADR-006-postgres-control-plane.md) — C# services interact with Postgres via Npgsql/Entity Framework Core.
