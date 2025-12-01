# ADR-006: Postgres/Aurora for Control Plane

**Status:** Accepted
**Date:** 2026-05-30
**Deciders:** Solo founder

---

## Purpose

Choose the database for the platform's control plane — user accounts, workspaces, domains, billing, permissions, and audit logs.

## Context

The platform has two distinct data domains:
1. **Hot path (redirects):** Key-value lookups by `hostname + slug`. Single-digit-ms latency, spiky read-heavy traffic, no joins. → [ADR-002: DynamoDB](ADR-002-dynamodb-redirect-lookups.md).
2. **Control plane (management):** Users, workspaces, domains, billing, permissions, audit logs. Relational data with foreign keys, joins, and transactional consistency.

The control plane needs:
- Relational integrity (a link belongs to a domain, which belongs to a workspace, which belongs to a user)
- Rich querying (list all domains for a workspace with certificate status, filter links by creation date and tags)
- Transactions (creating a workspace + default domain + initial links atomically)
- Migrations (schema evolves with the product)

Candidate stores evaluated:

| Store | Strengths | Weaknesses |
|---|---|---|
| **Postgres/Aurora** | ACID, joins, migrations, rich querying, familiar | Always-on cost ($15+/month minimum), operational overhead (backups, patching) |
| **DynamoDB-only** | Single database to operate, zero-cost-when-idle | No joins, no ad-hoc queries, complex single-table modeling for relational data, GSI explosion for multi-dimensional queries |
| **SQLite (embedded)** | Zero ops, zero cost, transactional | Not suitable for multi-instance Lambda deployment; concurrent write limitations |

## Approach

Use **PostgreSQL**, provisioned via **AWS Aurora Serverless v2** for the control plane. Aurora Serverless v2 scales to zero (zero capacity units = zero compute cost) while maintaining storage. The minimum ACU configuration means there is a floor cost during active use, but the database pauses when the application is idle.

Organize the schema around clear bounded contexts (users, workspaces, domains, links, billing) with explicit foreign key relationships. Run migrations using a standard migration tool (e.g., `dbmate` or Entity Framework Core migrations).

The redirect hot path does **not** depend on Postgres. If Postgres is unavailable, redirects continue to work via DynamoDB. See the reliability design in [ADR-002](ADR-002-dynamodb-redirect-lookups.md).

## Constraints

- Aurora Serverless v2 has a **minimum capacity of 0.5 ACU** when active (~$30/month with 0 ACU minimum when paused). This is the floor cost for the control plane — acceptable because the control plane is required for any user-facing functionality.
- Aurora Serverless v2 scaling is not instant. Cold start from paused state takes ~30 seconds. This is acceptable for the management dashboard (users experience a delay on first load after idle, not on critical paths).
- Multi-region availability requires Aurora Global Database, which adds cost and complexity. Defer until required.
- DynamoDB and Postgres dual-write consistency is not guaranteed. The control plane writes to both (link creation writes to Postgres for management and DynamoDB for redirects). If one write fails, the system should retry or queue for reconciliation.

## Decisions

1. **Postgres over DynamoDB-only** — The control plane's relational data patterns (joins, foreign keys, ad-hoc queries, migrations) are a natural fit for SQL. Forcing them into DynamoDB single-table design would create an unmaintainable tangle of GSIs and composite keys for what are straightforward SQL queries.
2. **Aurora Serverless v2 over RDS provisioned** — Serverless scaling reduces operational burden and cost during low-traffic periods. Provisioned RDS charges per hour regardless of load.
3. **Aurora Serverless v2 over Supabase/managed Postgres** — Minimizes third-party SaaS dependencies. AWS-native means unified billing, IAM integration, and VPC security. See [ADR-003](ADR-003-serverless-first-architecture.md).
4. **Separate control plane from hot path** — The redirect API only queries DynamoDB. Postgres downtime does not affect redirects. This is a deliberate reliability boundary.

## Trade-offs

| Trade-off | Detail |
|---|---|
| **Cost floor** | Aurora Serverless v2 has a non-zero cost floor (~$30/month minimum when active, plus storage). This violates the strictest interpretation of zero-cost-when-idle but is acceptable as the control plane is a necessary component. A free tier with 1 domain and 1,000 links can operate within this budget. |
| **Dual-write complexity** | Link creation writes to both Postgres and DynamoDB. This introduces eventual consistency concerns — if Postgres write succeeds and DynamoDB write fails, the link is invisible to the redirect engine. Mitigation: write to DynamoDB first (redirect path correctness), then to Postgres; if Postgres fails, queue for retry. |
| **Operational overhead** | Even with Aurora Serverless, Postgres requires migrations, connection management, and backup verification. This is inherent to relational databases — the alternative (DynamoDB-only) avoids this but sacrifices query flexibility. |
| **Scaling limits** | Aurora Serverless v2 scales to 128 ACUs. For a management dashboard with thousands of tenants, this is more than sufficient. If scaling becomes a bottleneck, the control plane can be sharded by tenant (not needed for MVP). |

## Evolution

- If Aurora Serverless v2 costs become disproportionate to the value it provides, evaluate whether the control plane schema is simple enough to migrate to DynamoDB single-table (unlikely given the relational nature) or whether a lighter-weight Postgres host (e.g., Railway, Fly.io Postgres) reduces the floor cost.
- If multi-region is needed for the control plane, enable Aurora Global Database for disaster recovery.
- Extract a **reconciliation service** that periodically scans Postgres and DynamoDB for consistency, repairing any dual-write failures.

## Related ADRs

- [ADR-002: DynamoDB for Redirect Lookups](ADR-002-dynamodb-redirect-lookups.md) — Dual-database design. DynamoDB for the hot path, Postgres for the control plane.
- [ADR-003: Serverless-First Architecture](ADR-003-serverless-first-architecture.md) — Aurora Serverless v2 is the closest Postgres gets to serverless, accepting a cost floor.
- [ADR-004: C# for Backend Services](ADR-004-csharp-backend.md) — C# services interact with Postgres via Npgsql/EF Core.
