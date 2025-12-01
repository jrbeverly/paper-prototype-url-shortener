# ADR-003: Serverless-First Architecture

**Status:** Accepted
**Date:** 2026-05-30
**Deciders:** Solo founder

---

## Purpose

Choose the compute and integration model for the platform — specifically whether to default to serverless (Lambda, API Gateway) or container-based (ECS/Fargate) services for backend workloads.

## Context

The platform is being built by a solo founder with no existing user base. Traffic will start at zero and grow unpredictably. The business model involves a free tier, so infrastructure must scale down to near-zero cost when unused.

Core workloads:
- **Redirect API:** Look up links and issue 302/307 redirects. Spiky, latency-sensitive, read-heavy.
- **Management API:** CRUD for links, domains, workspaces. Steady, low-volume, write-light.
- **Analytics ingestion:** Stream click events. High throughput, asynchronous.
- **Background jobs:** Certificate validation polling, abuse scanning, billing sync.

## Approach

Default to **AWS Lambda + API Gateway** (or Lambda Function URLs for internal services) for all compute. Use **EventBridge + SQS** for async workloads. Reserve ECS/Fargate only for workloads that exceed Lambda's 15-minute execution limit or require persistent connections.

Lambda functions are organized per bounded context, deployed independently, and configured with Graviton2 (ARM) runtimes for cost efficiency.

## Constraints

- Lambda has a 15-minute maximum execution timeout. Certificate provisioning polling and abuse scans must be designed to complete within this window or chain invocations.
- Lambda has a 6 MB invocation payload limit (request + response). Link bulk imports and analytics exports must use S3 pre-signed URLs rather than passing large payloads through Lambda.
- Cold starts add latency (~100ms-1s for .NET). This is acceptable for the management API but the redirect API may benefit from provisioned concurrency or CloudFront edge caching.
- API Gateway has a 29-second integration timeout.

## Decisions

1. **Lambda over ECS/Fargate** — Lambda's pay-per-request model aligns with zero-cost-when-idle. ECS/Fargate has a minimum cost floor (~$15-30/month for an empty cluster). At zero traffic, Lambda costs $0; Fargate does not.
2. **API Gateway HTTP API over REST API** — HTTP API is cheaper ($1.00/million vs $3.50/million requests) and has lower latency. REST API's richer feature set (usage plans, WAF integration at the API Gateway level) is not needed initially.
3. **EventBridge for service-to-service communication** — Avoids synchronous coupling between services. The redirect service emits events; the analytics service consumes them independently.
4. **Graviton2 (ARM) runtimes** — ~20% cost reduction and better performance per watt vs x86 for .NET and Node.js Lambda runtimes.

## Trade-offs

| Trade-off | Detail |
|---|---|
| **Cold starts** | .NET Lambda cold starts are slower than Node.js or Go. Mitigated by provisioned concurrency for the redirect API if latency becomes an issue, and by CloudFront edge caching of redirect responses. |
| **Vendor lock-in** | Lambda + EventBridge + API Gateway are AWS-specific. The business logic is isolated in handler functions that could be ported to containers or other FaaS platforms with minimal changes. |
| **Debugging complexity** | Distributed, event-driven systems are harder to debug locally than monolithic containers. Mitigated by structured logging, OpenTelemetry tracing, and integration tests that exercise the full event chain. |
| **Timeout ceiling** | Background jobs are bounded by 15-minute Lambda limits. This covers certificate polling, abuse scanning (chunked), and billing sync. If a job genuinely needs longer, Fargate is the escape hatch. |

## Evolution

- If Lambda cold starts become a user-visible problem on the redirect path, add provisioned concurrency for the redirect function or move the hot lookup to CloudFront Functions + KVS cache.
- If a workload exceeds Lambda limits (e.g., a bulk analytics export taking 30+ minutes), deploy that specific workload to Fargate as a one-off task, not a cluster migration.
- If EventBridge becomes expensive at very high event volumes (> billions/month), evaluate direct Kinesis-to-Lambda streaming for the analytics pipeline.

## Related ADRs

- [ADR-001: CloudFront SaaS Manager](ADR-001-cloudfront-saas-manager.md) — CloudFront is the edge compute complement to Lambda origins.
- [ADR-002: DynamoDB for Redirect Lookups](ADR-002-dynamodb-redirect-lookups.md) — DynamoDB's serverless billing model aligns with this decision.
- [ADR-004: C# for Backend Services](ADR-004-csharp-backend.md) — Lambda natively supports .NET 8 with Graviton2.
- [ADR-007: CloudFront Real-Time Logs for Analytics](ADR-007-cloudfront-real-time-logs-analytics.md) — Serverless analytics pipeline using Kinesis and Lambda.
