# ADR-007: CloudFront Real-Time Logs for Analytics

**Status:** Accepted
**Date:** 2026-05-30
**Deciders:** Solo founder

---

## Purpose

Choose the analytics data pipeline for capturing click events, processing them into aggregates, and serving dashboard data — all without degrading redirect performance.

## Context

Every click on a short link generates an analytics event (timestamp, tenant, domain, slug, destination, country, device, referrer, UTM params, bot score, status code, latency). At mature scale (1B clicks/month), this is ~380 events/second sustained.

Analytics must:
- Capture events reliably without adding latency to the redirect
- Scale independently from the redirect path (no coupling)
- Support both real-time dashboards and historical aggregate queries
- Have cost that tracks usage (free tier → low cost, enterprise → pay per volume)

Two architectural patterns were evaluated:

| Mode | How it works | Pros | Cons |
|---|---|---|---|
| **Origin-counted (Mode A)** | Every click hits the redirect API; the API emits an event to the analytics pipeline | Exact event control, easy enrichment | No edge caching possible, higher origin load, higher latency and cost |
| **Edge-log analytics (Mode B)** | Cache redirects at CloudFront; use CloudFront real-time logs for click events | Redirects are cached (faster, cheaper), analytics decoupled from origin | Less control over event enrichment, log sampling considerations |

## Approach

Use **Mode B: CloudFront real-time logs** as the primary analytics pipeline. CloudFront real-time logs deliver records to **Kinesis Data Streams** within seconds, with configurable sampling from 1-100%.

Pipeline architecture:

```
CloudFront real-time logs
  → Kinesis Data Streams
  → Kinesis Data Firehose
  → S3 (raw event lake, Parquet format)
  → ClickHouse / Athena (aggregates)
  → Dashboard API (serves pre-computed aggregates)
```

Tracked fields: timestamp, tenant ID, domain, slug, destination, country/region, device type, browser, referrer, UTM values, bot score, status code, latency.

For paid plans, store raw click data for the plan's retention period. For free plans, retain only aggregates after 30 days.

## Constraints

- CloudFront real-time logs have configurable sampling (1-100%). At 100% sampling, the log volume equals the request volume. At lower sampling rates, analytics are estimates — acceptable for free tiers, not for enterprise billing.
- Log delivery to Kinesis adds a small but non-zero delay (typically < 5 seconds). Real-time dashboards may lag by this interval.
- CloudFront logs contain the request fields CloudFront sees (headers, query strings, edge location). Custom enrichment (tenant metadata, link metadata) that isn't in the request must be joined later in the pipeline.
- Kinesis and Firehose have per-shard and per-stream throughput limits. At 1B events/month, this requires capacity planning for the stream configuration.

## Decisions

1. **Edge-log analytics over origin-counted analytics** — Separating analytics from the redirect path means redirects can be cached at CloudFront (lower latency, lower origin cost) while analytics are captured independently. The redirect API does not need to be invoked for every click — only for cache misses.
2. **Kinesis over direct S3 log delivery** — Real-time logs require Kinesis as the delivery destination. S3 access logs are cheaper but have hours of delay, which is unacceptable for a dashboard that shows "clicks in the last hour."
3. **S3 + ClickHouse/Athena over a single analytics database** — S3 is the cheapest durable store for raw events. ClickHouse (or Athena for simpler queries) handles aggregate queries on-demand. This separates storage cost from query cost.
4. **Configurable sampling over 100% capture** — Free-tier tenants use 10% sampling (acceptable for trend dashboards). Paid tiers use 100% sampling (exact counts for billing and reporting). Sampling rate is configured per distribution tenant.

## Trade-offs

| Trade-off | Detail |
|---|---|
| **Less control over events** | CloudFront logs capture what CloudFront sees, not what the origin sees. Custom enrichment (e.g., whether a redirect rule was triggered) that depends on origin logic is not in the logs. Mitigated by joining log data with control plane metadata during aggregation. |
| **Pipeline complexity** | Kinesis → Firehose → S3 → ClickHouse is more complex than writing events from the redirect API directly to a database. This complexity is acceptable because it decouples analytics from the redirect path — analytics can fail without breaking redirects. |
| **Cost at scale** | Kinesis Data Streams charges per shard-hour. At low traffic, this is the dominant cost. At high traffic, the per-GB Firehose and S3 storage costs become more significant. Cost should be tracked per tenant and used to inform pricing. |
| **Query latency** | Athena queries over S3 have seconds-to-minutes latency. For near-real-time dashboards, use ClickHouse (columnar, sub-second queries over billions of rows) or pre-compute aggregates in DynamoDB/Redis for the top-N queries. |

## Evolution

- If Kinesis costs become disproportionate at low traffic, evaluate whether S3 access logs with hourly batch processing are sufficient for the MVP dashboard (trade freshness for cost).
- If real-time dashboards require sub-second freshness, add a Lambda function triggered by Kinesis that updates a Redis/DynamoDB aggregate cache.
- If analytics query patterns become well-defined, pre-compute aggregates (hourly, daily, per-link, per-domain) and store them in DynamoDB, avoiding ad-hoc query engines entirely for common dashboard views.
- Evaluate **ClickHouse Cloud** vs self-hosted ClickHouse on ECS based on operational burden vs cost.

## Related ADRs

- [ADR-001: CloudFront SaaS Manager](ADR-001-cloudfront-saas-manager.md) — The same CloudFront distribution that serves redirects also generates the analytics logs.
- [ADR-002: DynamoDB for Redirect Lookups](ADR-002-dynamodb-redirect-lookups.md) — Analytics data is stored separately from redirect data; the two data domains are independent.
- [ADR-003: Serverless-First Architecture](ADR-003-serverless-first-architecture.md) — Kinesis + Firehose + S3 + Lambda are all serverless/managed services that scale to zero.
