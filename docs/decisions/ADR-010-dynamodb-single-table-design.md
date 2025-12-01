# ADR-010: DynamoDB Single-Table Design

**Status:** Accepted
**Date:** 2026-05-30
**Deciders:** Solo founder

---

## Purpose

Define the specific single-table design for the redirect hot path DynamoDB table — the key schema, Global Secondary Indexes (GSIs), item structure, and entity patterns that support all required access patterns.

## Context

[ADR-002](ADR-002-dynamodb-redirect-lookups.md) established DynamoDB as the primary store for redirect lookups and defined the high-level access pattern (`hostname#slug → redirect config`). This ADR specifies the concrete single-table design: the exact key structure, GSIs, entity modeling, and converter patterns.

The redirect table must support these access patterns:

| # | Pattern | Frequency | Latency target |
|---|---|---|---|
| P1 | Lookup redirect by hostname + slug | Hot path, every click | < 10ms P99 |
| P2 | List all links for a tenant (dashboard) | Medium, management UI | < 50ms P99 |
| P3 | List all links for a domain (management) | Low, domain detail view | < 50ms P99 |
| P4 | Get single link for editing | Low, edit form | < 10ms P99 |
| P5 | Batch get links by IDs | Low, bulk operations | < 50ms P99 |

The control plane data (users, workspaces, domains, billing, audit logs) is stored in Postgres, per [ADR-006](ADR-006-postgres-control-plane.md). This ADR covers only the redirect/link table.

## Approach

Use a **single DynamoDB table** with composite key design and type-prefixed partition keys, following the patterns in [.claude/guidelines/dynamodb.md](../../.claude/guidelines/dynamodb.md).

### Key Schema

```
Table: redirects-{environment}
Primary Key: PK (Partition Key, String), SK (Sort Key, String)
GSI1: GSI1PK (Partition Key, String), GSI1SK (Sort Key, String)
```

### Item Types and Key Structures

| Item | PK | SK | GSI1PK | GSI1SK |
|---|---|---|---|---|
| Link | `HOST#{hostname}#SLUG#{slug}` | `CONFIG` | `TENANT#{tenant_id}` | `DOMAIN#{domain_id}#SLUG#{slug}` |
| Domain metadata | `HOST#{hostname}` | `METADATA` | `TENANT#{tenant_id}` | `DOMAIN#{domain_id}` |
| Rate limit counter | `HOST#{hostname}` | `RATELIMIT#{window}` | — | — |

### Access Pattern Coverage

| Pattern | Query | Index |
|---|---|---|
| **P1: Lookup by hostname + slug** | `GetItem(PK=HOST#{hostname}#SLUG#{slug}, SK=CONFIG)` | Table (single read) |
| **P2: List links for tenant** | `Query(GSI1PK=TENANT#{tenant_id})` | GSI1 |
| **P3: List links for domain** | `Query(GSI1PK=TENANT#{tenant_id}, GSI1SK begins_with DOMAIN#{domain_id}#)` | GSI1 |
| **P4: Get single link for editing** | `GetItem(PK=HOST#{hostname}#SLUG#{slug}, SK=CONFIG)` | Table (same as P1) |
| **P5: Batch get links** | `BatchGetItem([{PK, SK}, {PK, SK}, ...])` | Table (batch read) |

### Link Item Structure

```json
{
  "PK": { "S": "HOST#go.customer.com#SLUG#summer-sale" },
  "SK": { "S": "CONFIG" },
  "EntityType": { "S": "Link" },
  "GSI1PK": { "S": "TENANT#tenant_123" },
  "GSI1SK": { "S": "DOMAIN#domain_456#SLUG#summer-sale" },
  "TenantId": { "S": "tenant_123" },
  "DomainId": { "S": "domain_456" },
  "Hostname": { "S": "go.customer.com" },
  "Slug": { "S": "summer-sale" },
  "DestinationUrl": { "S": "https://customer.com/landing-page" },
  "RedirectType": { "N": "302" },
  "Status": { "S": "active" },
  "RulesJson": { "S": "{\"geo\":[],\"device\":[],\"ab_test\":[]}" },
  "ExpiresAt": { "NULL": true },
  "CreatedAt": { "S": "2026-05-30T10:00:00Z" },
  "UpdatedAt": { "S": "2026-05-30T10:00:00Z" }
}
```

### Entity Converter Pattern

Domain models are separated from DynamoDB items using the converter pattern. Each entity type has:

- **Domain model** (in `Domain/`): Pure C# record, no persistence concerns
- **Entity class** (in `Data/Entities/`): Implements `IDynamoEntity`, maps to DynamoDB item structure
- **Converter** (static class): `ToAttributes(Entity)` and `FromAttributes(attributes)` bidirectional mapping

```csharp
// Domain model (src/UrlShortener/UrlShortener.Domain/Models/Link.cs)
public record Link
{
    public required Guid Id { get; init; }
    public required string TenantId { get; init; }
    public required string DomainId { get; init; }
    public required string Hostname { get; init; }
    public required string Slug { get; init; }
    public required string DestinationUrl { get; init; }
    public int RedirectType { get; init; } = 302;
    public string Status { get; init; } = "active";
    public string? RulesJson { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

// DynamoDB entity (src/UrlShortener/Data/Entities/LinkEntity.cs)
public class LinkEntity : IDynamoEntity
{
    public string PK { get; set; } = null!;
    public string SK { get; set; } = null!;
    public string EntityType { get; set; } = "Link";
    public string? Data { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // GSI attributes
    public string? GSI1PK { get; set; }
    public string? GSI1SK { get; set; }

    // Indexed properties
    public string TenantId { get; set; } = null!;
    public string DomainId { get; set; } = null!;
    public string Hostname { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string DestinationUrl { get; set; } = null!;
    public int RedirectType { get; set; } = 302;
    public string Status { get; set; } = "active";
    public string? RulesJson { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public static LinkEntity FromDomain(Link link)
    {
        return new LinkEntity
        {
            PK = $"HOST#{link.Hostname}#SLUG#{link.Slug}",
            SK = "CONFIG",
            GSI1PK = $"TENANT#{link.TenantId}",
            GSI1SK = $"DOMAIN#{link.DomainId}#SLUG#{link.Slug}",
            Data = JsonSerializer.Serialize(link),
            TenantId = link.TenantId,
            DomainId = link.DomainId,
            Hostname = link.Hostname,
            Slug = link.Slug,
            DestinationUrl = link.DestinationUrl,
            RedirectType = link.RedirectType,
            Status = link.Status,
            RulesJson = link.RulesJson,
            ExpiresAt = link.ExpiresAt,
            CreatedAt = link.CreatedAt,
            UpdatedAt = link.UpdatedAt
        };
    }

    public Link ToDomain()
    {
        if (string.IsNullOrEmpty(Data))
            throw new InvalidOperationException("Link data is missing");
        return JsonSerializer.Deserialize<Link>(Data)
            ?? throw new InvalidOperationException("Failed to deserialize link");
    }
}
```

The full domain model is serialized to the `Data` field as JSON. Selected properties (`TenantId`, `Hostname`, `Slug`, `DestinationUrl`, `Status`) are also stored as top-level attributes for indexed queries without requiring JSON deserialization.

See [.claude/guidelines/dynamodb.md](../../.claude/guidelines/dynamodb.md) for the complete converter pattern, including `IDynamoEntity`, `DynamoEntityConverter`, `DynamoKeyHelpers`, and the source generator.

## Constraints

- **400 KB item limit:** DynamoDB items are limited to 400 KB. A link with complex rules (geo, device, A/B, UTM) must fit within this. If rules grow beyond this, store rule references (S3 key or separate table item) and resolve them in a second step.
- **GSI projection:** GSI1 projects all attributes (`ProjectionType = ALL`) to support dashboard queries that need full link details. This doubles storage cost for indexed items — acceptable because links are small (< 5 KB typical) and the management dashboard needs full item data.
- **Hot partition risk:** If a single hostname + slug combination receives disproportionate traffic (e.g., a viral link), the DynamoDB partition can throttle. Mitigation: DynamoDB adaptive capacity distributes throughput to hot partitions automatically for on-demand tables. If a single key exceeds 3,000 RCU/second, add a cache shard pattern.
- **Key length:** DynamoDB partition keys have a practical limit of ~2 KB. The composite key `HOST#{hostname}#SLUG#{slug}` is well within this (hostname max 253 bytes, slug max ~100 bytes).

## Decisions

1. **Single table over multiple tables** — All redirect-related items (links, domain metadata, rate limit counters) live in one table. This eliminates cross-table joins (DynamoDB doesn't do joins), enables single-read lookups, and simplifies backup/restore. Separate domains already have their own store: control plane in Postgres (ADR-006), analytics in S3 (ADR-007).

2. **Composite PK with hostname + slug over hash key** — The composite key `HOST#{hostname}#SLUG#{slug}` is human-readable, grep-friendly in logs, and enables single-`GetItem` lookups. A hash-based key (SHA-256 of hostname + slug) would be compact but opaque — debugging a redirect failure requires hashing the input to find the item.

3. **GSI1 for tenant-scoped queries over scan operations** — Scanning the entire table to list a tenant's links would be expensive at scale. GSI1 with partition key `TENANT#{tenant_id}` enables efficient queries scoped to a single tenant, with `begins_with` filtering on the sort key for domain-specific queries.

4. **Entity converter pattern over raw DynamoDB SDK usage** — The converter pattern (`FromDomain` / `ToDomain`) keeps domain models pure and persistence concerns in infrastructure. This is a deliberate trade of boilerplate for testability and portability. See [.claude/guidelines/dynamodb.md](../../.claude/guidelines/dynamodb.md).

5. **Data field as full JSON snapshot over attribute-per-field** — The `Data` field stores the complete domain model as JSON. This means `ToDomain()` is always a deserialization rather than field-by-field reassembly. Indexed fields are duplicated as top-level attributes for queries. Trade-off: storage overhead (~15-20%) for simplicity and resilience to schema changes.

## Trade-offs

| Trade-off | Detail |
|---|---|
| **GSI cost** | GSI1 doubles the effective storage cost for indexed items. For 2 million links at ~2 KB each, this is ~8 GB total (table + GSI). At DynamoDB storage pricing ($0.25/GB-month), this is ~$2/month — negligible compared to request costs. |
| **Key readability vs compactness** | Composite keys with type prefixes (`HOST#`, `SLUG#`, `TENANT#`) are self-documenting but consume more bytes than compact keys. For 2 million links with ~80-byte keys, key storage is ~160 MB — negligible. |
| **Access pattern rigidity** | Single-table design requires upfront access pattern modeling. Adding a new query pattern (e.g., "list links expiring this week") requires a new GSI, which must be provisioned and populated. If new patterns are frequent, this design becomes a bottleneck. Mitigation: the redirect hot path's access patterns are well-understood and stable. |
| **Dual-write with Postgres** | Link creation writes to both Postgres (control plane) and DynamoDB (redirect path). If one fails, the system is inconsistent. Mitigation: write to DynamoDB first (redirect correctness is paramount), then to Postgres. A reconciliation process repairs inconsistencies. See [ADR-006](ADR-006-postgres-control-plane.md). |

## Evolution

- If a link's rules grow beyond the 400 KB item limit, split rules into a separate DynamoDB table or S3 object. The link's `RulesJson` field becomes a reference key rather than inline JSON. The redirect service performs a second lookup when rules are present — this is rare (most links have no rules).
- If GSI1 query patterns become more diverse, add a GSI2 for additional access patterns (e.g., by creation date, by status). GSIs should be added sparingly — each one has cost and throughput implications.
- If the analytics team needs to query links by dimensions not covered by GSIs, stream DynamoDB changes (via DynamoDB Streams) to OpenSearch rather than adding more GSIs.
- If the entity converter boilerplate becomes burdensome as entity types grow, adopt the source generator pattern (`[GenerateConverter]` attribute) defined in [.claude/guidelines/dynamodb.md](../../.claude/guidelines/dynamodb.md).

## Related ADRs

- [ADR-002: DynamoDB for Redirect Lookups](ADR-002-dynamodb-redirect-lookups.md) — Established DynamoDB as the redirect store. This ADR defines the specific schema for that store.
- [ADR-003: Serverless-First Architecture](ADR-003-serverless-first-architecture.md) — DynamoDB on-demand billing aligns with zero-cost-when-idle.
- [ADR-006: Postgres for Control Plane](ADR-006-postgres-control-plane.md) — Postgres handles the control plane; this table handles the hot path only.
- [ADR-007: CloudFront Real-Time Logs for Analytics](ADR-007-cloudfront-real-time-logs-analytics.md) — Analytics data is stored separately from redirect data.
