# ADR-002: DynamoDB for Redirect Lookups

**Status:** Accepted
**Date:** 2026-05-30
**Deciders:** Solo founder

---

## Purpose

Choose the primary data store for the redirect hot path: resolving `hostname + slug → redirect configuration` in under single-digit milliseconds to minimize latency on every click.

## Context

The redirect engine is the critical hot path. Every visitor click hits this lookup. At mature scale (1B clicks/month), this is ~380 lookups/second sustained, with bursts potentially much higher.

The lookup key is simple: `hostname#slug`. The result is a redirect configuration (destination URL, redirect type, rules, expiration). No joins, no complex queries, no transactions — a pure key-value access pattern.

Candidate stores evaluated:

| Store | Strengths | Weaknesses |
|---|---|---|
| **DynamoDB** | Single-digit ms latency, serverless, auto-scaling, pay-per-request | No joins, limited query patterns, provisioned throughput requires planning |
| **Redis/ElastiCache** | Sub-ms latency, rich data structures | Always-on cost (~$30+/month minimum), operational overhead, data in memory |
| **Postgres/Aurora** | Rich queries, transactions, familiar | Always-on cost, connection management, overkill for key-value access |
| **CloudFront KVS** | Edge-native, lowest latency | 5 MB/store, 1 KB values, not designed for millions of entries |

## Approach

Use **DynamoDB with on-demand billing** as the primary redirect lookup store, with a single-table design:

```
PK = TENANT#{tenant_id}#DOMAIN#{hostname}#SLUG#{slug}
```

This partition key gives a single-read `GetItem` lookup for the hot path. A Global Secondary Index on `tenant_id + slug` supports the management API (list links by tenant).

CloudFront Functions may cache a tiny subset of the hottest links at the edge, but the DynamoDB table is the source of truth.

## Constraints

- DynamoDB items are limited to 400 KB. Redirect configurations with complex rules (geo, device, A/B) must stay within this limit. If rules grow beyond this, store rule references and resolve them in a second step.
- Single-table design requires access-pattern-first modeling. New query patterns may require new GSIs, which must be planned before data exists.
- On-demand billing is cost-effective at low traffic but becomes expensive at very high sustained throughput. Monitor and consider switching to provisioned capacity with auto-scaling if costs exceed projections.

## Decisions

1. **DynamoDB over Redis** — Zero-cost-when-idle beats sub-ms latency gains for this use case. A cold start DynamoDB read (~10ms) is acceptable for redirects; Redis would cost $30+/month even at zero traffic. See [ADR-003](ADR-003-serverless-first-architecture.md).
2. **DynamoDB over CloudFront KVS** — KVS size limits (5 MB per store) make it unsuitable as the primary link database for millions of links. KVS may be used as an edge cache for the top-N hottest links.
3. **DynamoDB over Postgres for the hot path** — DynamoDB has no connection limits, no always-on cost, and single-digit-ms latency without tuning. Postgres is reserved for the control plane. See [ADR-006](ADR-006-postgres-control-plane.md).
4. **On-demand billing initially** — Eliminates capacity planning. Switch to provisioned with auto-scaling once traffic patterns are predictable and cost optimization matters.

## Trade-offs

| Trade-off | Detail |
|---|---|
| **Query flexibility** | DynamoDB cannot do ad-hoc queries or joins. Analytics and complex reporting must use a separate store — this is by design. |
| **Vendor lock-in** | DynamoDB is AWS-only. The access pattern is simple enough (key-value) that migrating to another KV store (e.g., ScyllaDB, FoundationDB) is feasible if needed. |
| **Cold start latency** | DynamoDB reads are ~1-10ms vs Redis <1ms. At redirect scale, the difference is not user-visible, especially behind CloudFront. |
| **GSI planning** | Adding a new access pattern after data exists requires a new GSI, which has its own provisioning and cost. Upfront access-pattern modeling is mandatory. |

## Evolution

- If redirect rules grow complex enough to exceed 400 KB per item, split rules into a separate table or S3 object referenced by the link record.
- If analytics requires real-time querying by dimensions not covered by GSIs, stream DynamoDB changes to OpenSearch or ClickHouse rather than adding more GSIs.
- If latency at the edge becomes critical, add a CloudFront KVS cache for the top 1,000-10,000 links hydrated from DynamoDB on cache miss.

## Related ADRs

- [ADR-001: CloudFront SaaS Manager](ADR-001-cloudfront-saas-manager.md) — CloudFront is the edge layer; DynamoDB is the origin lookup.
- [ADR-003: Serverless-First Architecture](ADR-003-serverless-first-architecture.md) — DynamoDB aligns with zero-cost-when-idle.
- [ADR-006: Postgres for Control Plane](ADR-006-postgres-control-plane.md) — Postgres handles relational data; DynamoDB handles the hot path.
- [ADR-007: CloudFront Real-Time Logs for Analytics](ADR-007-cloudfront-real-time-logs-analytics.md) — Analytics are separated from the lookup store.
