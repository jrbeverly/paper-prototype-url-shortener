# ADR-011: Analytics Schema and Partitioning Strategy

**Status:** Accepted
**Date:** 2026-05-30
**Deciders:** Solo founder

---

## Purpose

Define the analytics data schema, S3 partitioning strategy, aggregate storage model, and retention policies — the specifics of how click events flow from ingestion to dashboard queries.

## Context

[ADR-007](ADR-007-cloudfront-real-time-logs-analytics.md) established the analytics pipeline (CloudFront real-time logs → Kinesis → Firehose → S3 → ClickHouse/Athena) and the decision to decouple analytics from the redirect hot path. This ADR defines the concrete schema, partitioning, and storage decisions within that pipeline.

The analytics system must support:

| Query type | Example | Freshness | Retention |
|---|---|---|---|
| **Real-time counter** | "How many clicks in the last hour?" | < 5 minutes | Rolling |
| **Link-level aggregates** | "Total clicks on `/summer-sale` by country this week" | < 1 hour | Plan-dependent |
| **Time-series dashboard** | "Daily click trend for the past 30 days" | < 1 hour | Plan-dependent |
| **Raw event export** | "Export all clicks for domain X in June 2026" | < 24 hours | Enterprise only |

## Approach

### Raw Event Storage (S3)

Raw click events land in S3 via Kinesis Data Firehose, partitioned for efficient querying:

**S3 Path Pattern:**
```
s3://analytics-{environment}/
  raw/
    year={YYYY}/
      month={MM}/
        day={DD}/
          hour={HH}/
            tenant_id={tenant_id}/
              {firehose-delivery-id}.parquet
```

**Why this partition hierarchy:**
- `year/month/day/hour` — Time-range queries are the most common access pattern (dashboards filter by date). Partitioning by date enables Athena/ClickHouse to prune partitions and avoid scanning irrelevant data.
- `tenant_id` — Tenant-scoped queries (e.g., "show me my dashboard") filter to a single tenant. Partitioning by tenant prevents cross-tenant scans.
- `hour` granularity — Balances partition size (~100-1,000 files/hour at scale) with query pruning efficiency. Finer granularity (minute) creates too many small files; coarser (day) makes hourly dashboards scan entire days.

### Event Schema (Parquet)

Each click event is a row in a Parquet file with the following schema:

| Field | Type | Description |
|---|---|---|
| `event_id` | STRING | UUIDv7 (time-sortable) |
| `timestamp` | TIMESTAMP | UTC event time (from CloudFront log) |
| `tenant_id` | STRING | Tenant identifier |
| `domain` | STRING | Customer hostname (e.g., `go.customer.com`) |
| `slug` | STRING | Short path (e.g., `/summer-sale`) |
| `destination_url` | STRING | Target URL (redacted if sensitive query params) |
| `redirect_type` | INT | 301, 302, or 307 |
| `country` | STRING | ISO 3166-1 alpha-2 country code (from CloudFront geo headers) |
| `region` | STRING | CloudFront edge location |
| `device_type` | STRING | `desktop`, `mobile`, `tablet`, `bot` (from User-Agent parsing) |
| `browser` | STRING | Browser family (from User-Agent parsing) |
| `referrer_domain` | STRING | Referrer hostname (or `direct`) |
| `utm_source` | STRING | UTM source parameter |
| `utm_medium` | STRING | UTM medium parameter |
| `utm_campaign` | STRING | UTM campaign parameter |
| `bot_score` | INT | 0-100 bot probability (from CloudFront bot control or custom logic) |
| `status_code` | INT | HTTP status returned (200, 301, 302, 307, 404) |
| `latency_ms` | INT | Origin response latency in milliseconds |
| `ip_hash` | STRING | SHA-256 hash of client IP (not raw IP — privacy-preserving) |
| `user_agent_hash` | STRING | SHA-256 hash of full User-Agent string |

**Why Parquet:**
- Columnar format — queries that select only a few columns (e.g., `SELECT country, COUNT(*)`) read only those columns, reducing scan cost.
- Compression — typically 5-10x smaller than JSON, reducing S3 storage and Athena scan costs.
- Predicate pushdown — query engines can skip row groups based on column statistics (min/max values), further reducing scan cost.

### Aggregate Storage (DynamoDB)

Common dashboard queries pre-compute aggregates and store them in a separate DynamoDB table. This avoids running Athena/ClickHouse queries for every dashboard load.

**Aggregate Table:** `analytics-aggregates-{environment}`

**Key Schema:**

| Aggregate | PK | SK | TTL |
|---|---|---|---|
| Daily per-link | `LINK#{link_id}` | `DAY#2026-05-30` | 90 days |
| Daily per-domain | `DOMAIN#{domain_id}` | `DAY#2026-05-30` | 90 days |
| Daily per-tenant | `TENANT#{tenant_id}` | `DAY#2026-05-30` | 90 days |
| Hourly per-link (real-time) | `LINK#{link_id}` | `HOUR#2026-05-30T14` | 48 hours |

**Aggregate Item Structure (daily per-link):**

```json
{
  "PK": { "S": "LINK#link_abc123" },
  "SK": { "S": "DAY#2026-05-30" },
  "ClickCount": { "N": "1523" },
  "UniqueEstimate": { "N": "1201" },
  "Countries": { "S": "{\"US\":800,\"GB\":320,\"DE\":150,\"OTHER\":253}" },
  "Devices": { "S": "{\"mobile\":900,\"desktop\":500,\"tablet\":123}" },
  "Referrers": { "S": "{\"twitter.com\":400,\"direct\":600,\"facebook.com\":300}" },
  "Ttl": { "N": "1717113600" }
}
```

Aggregates are computed by a scheduled Lambda (EventBridge cron, hourly) that queries the previous hour's raw data and merges into the aggregate table. Real-time counters (current hour) are updated by a Kinesis-triggered Lambda that increments counters directly.

### Retention Policy

| Tier | Raw events (S3) | Aggregates (DynamoDB) | Real-time counters |
|---|---|---|---|
| **Free** | 30 days | 30 days | 24 hours |
| **Starter** | 90 days | 90 days | 48 hours |
| **Pro** | 1 year | 1 year | 7 days |
| **Team** | 2 years | 2 years | 14 days |
| **Business** | 5 years | 5 years | 30 days |
| **Enterprise** | Custom | Custom | Custom |

Retention is enforced by S3 lifecycle policies (delete objects older than N days) and DynamoDB TTL attributes (automatically expire items).

## Constraints

- **S3 partition limits:** Athena charges by data scanned. Deeply nested partitions help query performance by pruning data. However, S3 has no limit on partitions — the partitioning scheme is bounded by query patterns, not by S3 constraints.
- **Firehose buffer:** Kinesis Data Firehose buffers data before writing to S3 (buffer size: 128 MB or buffer interval: 900 seconds, whichever comes first). This means raw events may be delayed up to 15 minutes. Acceptable because the real-time counter path (Kinesis → Lambda → DynamoDB) bypasses S3 entirely.
- **Athena query cost:** Athena charges $5/TB scanned. With Parquet compression and partition pruning, a typical dashboard query scanning 30 days of a single tenant's data (partitioned by date and tenant) scans ~100 MB and costs ~$0.0005. For complex ad-hoc queries, cost can be higher. Enterprise customers with high query volume may justify dedicated ClickHouse.
- **DynamoDB aggregate write cost:** Each aggregate update is a write request. At 2 million links with hourly aggregate updates for real-time counters, this is 2M writes/hour (~550/second). At $1.25/million writes, this is ~$2.50/hour. Mitigation: real-time counters only for active links (clicked in the last 24 hours); stale links skip hourly updates.

## Decisions

1. **Parquet over JSON for raw storage** — Parquet's columnar format and compression reduce storage and query costs by 5-10x compared to JSON. The trade-off is that Parquet requires a processing step (Firehose conversion) and is not human-readable without tooling. Acceptable because raw events shouldn't be read directly by humans — they are queried through Athena/ClickHouse.

2. **Hierarchical date partitioning over flat structure** — `year/month/day/hour` pruning eliminates irrelevant partitions for date-range queries. Flat structures (single date prefix like `dt=2026-05-30`) can't prune by hour for intra-day queries.

3. **Pre-computed aggregates over on-demand queries for dashboards** — Dashboard views (tenant overview, link detail, domain stats) are pre-computed and stored in DynamoDB. Athena/ClickHouse is reserved for ad-hoc exploration and exports. This keeps dashboard loads fast and cheap (< 10ms DynamoDB read vs seconds-to-minutes Athena query).

4. **DynamoDB for aggregates over Redis/ElastiCache** — DynamoDB has no always-on cost, auto-scales, and stores aggregates persistently (not just cache). Redis would require always-on instances ($30+/month) and data that can be lost without consequence. Aggregates need persistence (recomputing from raw events is expensive). See [ADR-002](ADR-002-dynamodb-redirect-lookups.md) and [ADR-003](ADR-003-serverless-first-architecture.md).

5. **Tiered retention over uniform retention** — Free-tier retention (30 days) limits storage cost for non-paying users. Higher tiers retain data longer, creating a revenue-aligned cost model. S3 lifecycle policies and DynamoDB TTL enforce retention automatically.

6. **UUIDv7 for event_id over UUIDv4** — UUIDv7 encodes the creation timestamp in the UUID, making event IDs time-sortable. This is useful for ordered event replay and debugging without a separate timestamp index.

## Trade-offs

| Trade-off | Detail |
|---|---|
| **Pre-computation latency** | Hourly aggregate updates mean dashboards may show data up to 1 hour old (plus Firehose buffer delay). For the MVP, this is acceptable. For near-real-time dashboards, the real-time counter path (Kinesis → Lambda → DynamoDB) provides current-hour estimates updated within seconds. |
| **Aggregate rigidity** | Pre-computing aggregates for specific dimensions (country, device, referrer) means ad-hoc queries on other dimensions must go to Athena. If customers frequently request new dimensions (e.g., "clicks by browser version"), the aggregate schema must be updated and backfilled. Mitigation: the `Countries`, `Devices`, and `Referrers` fields store JSON maps (top-N + OTHER) that capture the most common query dimensions. |
| **Storage cost at high volume** | At 1B events/month, Parquet files (~200 bytes/event compressed) consume ~200 GB/month. At $0.023/GB-month, raw storage is ~$4.60/month for each month retained. The dominant cost is not storage but Athena queries — pre-computed aggregates minimize these. |
| **Parquet conversion overhead** | Firehose converts JSON → Parquet before writing to S3. This adds a small processing delay and cost (Firehose data format conversion is included in the per-GB delivery price). Acceptable because the query-time savings from Parquet far exceed the conversion cost. |

## Evolution

- If dashboard queries for pre-computed aggregates become a DynamoDB hot partition problem (e.g., a tenant with millions of links), shard the aggregate table by a hash of the link ID (e.g., `PK = LINK#{link_id}#SHARD_{0..9}`).
- If ad-hoc query volume makes Athena costs unpredictable, deploy ClickHouse (self-hosted on ECS or ClickHouse Cloud) for heavier query workloads. The S3 event lake in Parquet format is directly queryable by ClickHouse.
- If customers demand real-time dashboards with sub-second freshness, replace the hourly aggregate Lambda with a Kinesis-triggered Lambda that incrementally updates DynamoDB aggregates on each event. This increases write costs but eliminates latency.
- If analytics become a major revenue driver (e.g., enterprise customers paying for advanced analytics), evaluate a dedicated analytics database (ClickHouse Cloud) rather than S3 + Athena for the paid tier.

## Related ADRs

- [ADR-007: CloudFront Real-Time Logs for Analytics](ADR-007-cloudfront-real-time-logs-analytics.md) — Established the analytics pipeline. This ADR defines the schema and storage within that pipeline.
- [ADR-002: DynamoDB for Redirect Lookups](ADR-002-dynamodb-redirect-lookups.md) — DynamoDB serves both redirect lookups and aggregate analytics, using separate tables for separate domains.
- [ADR-003: Serverless-First Architecture](ADR-003-serverless-first-architecture.md) — S3 + Athena + DynamoDB are serverless; ClickHouse on ECS is the escape hatch.
- [ADR-010: DynamoDB Single-Table Design](ADR-010-dynamodb-single-table-design.md) — The aggregate table uses the same DynamoDB entity patterns as the redirect table.
