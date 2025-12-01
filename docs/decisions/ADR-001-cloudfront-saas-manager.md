# ADR-001: CloudFront SaaS Manager for Domain Management

**Status:** Accepted
**Date:** 2026-05-30
**Deciders:** Solo founder

---

## Purpose

Choose the edge/CDN layer for hosting 20,000+ customer custom domains in a multi-tenant link management platform.

## Context

The platform must allow each customer to connect a custom domain (e.g., `go.customer.com`) for branded short links. Standard Amazon CloudFront has hard quotas that make this impractical at scale:

- **100 alternate domain names per distribution** — a single distribution cannot serve 20,000 domains.
- **500 distributions per AWS account** — even if each distribution served 100 domains, you'd cap at 50,000 domains, and managing 200+ distributions is operationally heavy.
- **One ACM certificate per distribution** — cert management becomes a per-distribution burden.

AWS launched CloudFront SaaS Manager specifically for SaaS providers managing many customer domains. It provides reusable configurations, TLS certificate management, DDoS protection, and observability for multi-domain SaaS delivery.

## Approach

Use **CloudFront SaaS Manager** with **distribution tenants** — one tenant per customer domain. Tenants inherit shared TLS/security config or attach tenant-specific certificates. Each tenant can have one or more domains.

The default quota is **10,000 distribution tenants per AWS account** (requestable increase). For 20,000+ domains, either request a quota increase or split across two AWS accounts under AWS Organizations.

## Constraints

- CloudFront Functions at the edge have restricted runtime access (no network access) and limited KeyValueStore (5 MB per store, 1 KB values) — unsuitable for the full link database. Use them only for host/path normalization, blocking bad requests, reading a tiny hot-link cache, and routing to the correct origin. See [ADR-002](ADR-002-dynamodb-redirect-lookups.md).
- Certificate issuance and DNS validation are async — domain onboarding must poll for status.
- Distribution tenant limits must be monitored and quota increases requested early.

## Decisions

1. **CloudFront SaaS Manager over manual CloudFront distributions** — Distribution tenants eliminate the per-distribution domain/certificate limits and make tenant lifecycle management a first-class operation.
2. **Distribution tenants over separate distributions per customer** — One tenant per domain keeps the mental model simple without the overhead of provisioning full distributions per customer.
3. **Multi-account sharding at 10,000+ tenants** — Design the provisioning layer with an account router from day one, so crossing the default quota is a configuration change, not an architecture rewrite.

## Trade-offs

| Trade-off | Detail |
|---|---|
| **Lock-in** | CloudFront SaaS Manager is an AWS-specific service; migrating to another CDN would require rebuilding the tenant management layer. |
| **New service risk** | Launched April 2025 — fewer production references, potential for API churn. Mitigated by AWS's track record with CloudFront. |
| **Cost** | Distribution tenants and per-request pricing scale with usage; free tier may not cover high-volume tenants. Acceptable because costs track revenue. |
| **DNS onboarding complexity** | Customers must set CNAME records and possibly TXT validation records — this is inherent to any custom-domain SaaS, not specific to CloudFront. |

## Evolution

- Monitor CloudFront SaaS Manager quota increases and API stability.
- If AWS deprecates or significantly changes the service, the distribution tenant abstraction is thin enough to replatform (e.g., to a custom CloudFront distribution-per-tenant model or Cloudflare for SaaS).
- Once account sharding is needed, extract a **Domain Provisioning Service** that routes tenant creation to the correct AWS account.

## Implementation Reference

- [CloudFront SaaS Manager API Research](../research/cloudfront-saas-manager-api.md) — Full API surface, code examples, quotas, limitations, integration design.

## Related ADRs

- [ADR-002: DynamoDB for Redirect Lookups](ADR-002-dynamodb-redirect-lookups.md) — CloudFront Functions alone cannot serve as the link database.
- [ADR-003: Serverless-First Architecture](ADR-003-serverless-first-architecture.md) — Aligns with the zero-cost-when-idle principle.
- [ADR-007: CloudFront Real-Time Logs for Analytics](ADR-007-cloudfront-real-time-logs-analytics.md) — Uses the same CloudFront layer for both serving and analytics.
