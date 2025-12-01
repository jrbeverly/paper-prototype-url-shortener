# Architecture Decision Records

This directory contains Architecture Decision Records (ADRs) for the short-io-url-shortener platform. ADRs capture the context, rationale, and trade-offs behind major architectural choices.

Each ADR follows the CLOD-MD format: Purpose, Context, Approach, Constraints, Decisions, Trade-offs, Evolution.

## Index

| ADR | Title | Status |
|---|---|---|
| [ADR-001](ADR-001-cloudfront-saas-manager.md) | CloudFront SaaS Manager for Domain Management | Accepted |
| [ADR-002](ADR-002-dynamodb-redirect-lookups.md) | DynamoDB for Redirect Lookups | Accepted |
| [ADR-003](ADR-003-serverless-first-architecture.md) | Serverless-First Architecture | Accepted |
| [ADR-004](ADR-004-csharp-backend.md) | C# for Backend Services | Accepted |
| [ADR-005](ADR-005-vue-typescript-frontend.md) | Vue 3 + TypeScript for Frontend | Accepted |
| [ADR-006](ADR-006-postgres-control-plane.md) | Postgres/Aurora for Control Plane | Accepted |
| [ADR-007](ADR-007-cloudfront-real-time-logs-analytics.md) | CloudFront Real-Time Logs for Analytics | Accepted |
| [ADR-008](ADR-008-api-versioning-strategy.md) | API Versioning Strategy | Accepted |
| [ADR-009](ADR-009-authentication-jwt-api-keys.md) | Authentication Scheme (JWT + API Keys) | Accepted |
| [ADR-010](ADR-010-dynamodb-single-table-design.md) | DynamoDB Single-Table Design | Accepted |
| [ADR-011](ADR-011-analytics-schema-partitioning.md) | Analytics Schema and Partitioning Strategy | Accepted |
| [ADR-012](ADR-012-error-handling-rfc-7807.md) | Error Handling and RFC 7807 Adoption | Accepted |

## Decision Map

```
Edge/CDN ─────────────────────────────────────────────────────────────────
    ADR-001: CloudFront SaaS Manager
    ADR-007: CloudFront Real-Time Logs for Analytics (built on ADR-001)
    ADR-011: Analytics Schema and Partitioning (built on ADR-007)
         │
Redirect Hot Path ────────────────────────────────────────────────────────
    ADR-002: DynamoDB for Redirect Lookups
    ADR-003: Serverless-First (Lambda + API Gateway)
    ADR-010: DynamoDB Single-Table Design (key schema for ADR-002)
         │
Backend Services ─────────────────────────────────────────────────────────
    ADR-003: Serverless-First (Lambda + API Gateway)
    ADR-004: C# for Backend Services (runs on ADR-003)
    ADR-006: Postgres/Aurora for Control Plane (separate from ADR-002)
    ADR-008: API Versioning Strategy (applies to C# APIs from ADR-004)
    ADR-012: Error Handling and RFC 7807 (error format for all APIs)
         │
Frontend ─────────────────────────────────────────────────────────────────
    ADR-005: Vue 3 + TypeScript for Frontend
    (served via CloudFront from ADR-001)
         │
Cross-Cutting ────────────────────────────────────────────────────────────
    ADR-009: Authentication (JWT + API Keys) (applies to all services)
    ADR-012: Error Handling and RFC 7807 (applies to all services)
```

## Cross-Reference Matrix

### Layer 1: Infrastructure (ADRs 001-003, 006-007)

|  | ADR-001 | ADR-002 | ADR-003 | ADR-006 | ADR-007 |
|---|---|---|---|---|---|
| **ADR-001** | — | Edge cache, not full DB | CloudFront + Lambda origins | — | Same CloudFront layer |
| **ADR-002** | Edge → origin lookup | — | DynamoDB serverless | Separate from Postgres | Independent data domain |
| **ADR-003** | CloudFront origins | DynamoDB serverless | — | Aurora Serverless v2 | Serverless analytics |
| **ADR-006** | — | Dual-write | — | — | — |
| **ADR-007** | Real-time logs | Analytics ≠ lookups | Serverless analytics | — | — |
| **ADR-010** | — | Key schema defined here | On-demand billing | Dual-write consistency | — |
| **ADR-011** | — | — | — | — | Schema & partitioning |

### Layer 2: Application (ADRs 004-005, 008-009, 012)

|  | ADR-004 | ADR-005 | ADR-008 | ADR-009 | ADR-012 |
|---|---|---|---|---|---|
| **ADR-004** | — | TypeScript/C# split, OpenAPI | Namespace versioning in C# | BFF in C#, Cognito SDK | DomainException in C# |
| **ADR-005** | OpenAPI-generated types | — | Version-specific OpenAPI specs | JWT stored in BFF cookie | Error components in SPA |
| **ADR-008** | URL path + namespace | Separate OpenAPI per version | — | Auth via middleware | Consistent error format |
| **ADR-009** | BFF in C# Minimal API | BFF cookie for SPA | Applies across versions | — | Auth errors → 401/403 |
| **ADR-012** | Result pattern + exceptions | Problem Details consumed by SPA | Same format all versions | Auth errors use Problem Details | — |
