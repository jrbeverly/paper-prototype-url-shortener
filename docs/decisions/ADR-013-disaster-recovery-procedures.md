# ADR-013: Disaster Recovery Procedures

**Status:** Accepted
**Date:** 2026-06-01
**Deciders:** Solo founder

---

## Purpose

Define disaster recovery (DR) procedures for the platform — recovery point objectives (RPO), recovery time objectives (RTO), backup mechanisms for each data store, region failover strategy, and the DR testing cadence.

## Context

The platform uses a serverless-first architecture with three data stores, each with different durability and recovery characteristics:

| Store | Service | Technology | Criticality |
|---|---|---|---|
| Redirect lookup | DynamoDB | Single-table, on-demand | Critical — every click depends on it |
| Control plane | Aurora Serverless v2 | PostgreSQL | Critical — all management operations depend on it |
| Analytics data lake | S3 via Kinesis Firehose | Parquet files | Important — dashboards and billing depend on it |
| Infrastructure state | S3 + DynamoDB (Terraform) | Remote state + locking | Critical — cannot reconfigure infrastructure without it |

Compute (Lambda, API Gateway, CloudFront) is stateless and defined in Terraform — no data to recover, only configuration. Recovery means reapplying the Terraform plan.

The serverless architecture already provides inherent resilience: Lambda functions are multi-AZ by default, DynamoDB replicates across three AZs synchronously, and S3 has 99.999999999% (11 nines) durability. The DR procedures address scenarios beyond single-AZ failure: accidental data deletion, data corruption, region-wide outage, or total infrastructure loss.

## Approach

### Recovery Objectives

| Objective | Target | Rationale |
|---|---|---|
| **RPO (critical data)** | 5 minutes | DynamoDB PITR records changes continuously; Aurora automated backups run every 5 minutes |
| **RPO (analytics data)** | 15 minutes | S3 cross-region replication lag; acceptable for analytics freshness |
| **RTO (single data store)** | 2 hours | Restore DynamoDB table from PITR or Aurora cluster from backup, reconfigure application |
| **RTO (full regional outage)** | 4 hours | Terraform apply to DR region, data restore, DNS cutover, validation |

### Backup Mechanisms

| Store | Mechanism | Retention | Restore Method |
|---|---|---|---|
| DynamoDB (redirects) | Point-in-Time Recovery (PITR) | 35 days | Restore to new table from timestamp |
| Aurora Postgres (control plane) | Automated backups + transaction logs | 35 days (prod), 7 days (non-prod) | Restore to new cluster from snapshot or point-in-time |
| S3 (analytics) | Cross-region replication | Source: lifecycle policies; DR copy: matches source | Failover to replica bucket |
| S3 (Terraform state) | Versioning + same-region replication | All versions retained | Retrieve specific version or replica |
| DynamoDB (Terraform lock) | PITR (same as redirects table) | 35 days | Restore from timestamp |

### Region Failover Strategy

**Decision:** Accept regional outage as acceptable risk for the MVP. A full AWS region failure (us-east-1) is a rare event — the last significant one was December 2021. The cost of running hot standby infrastructure in a second region exceeds the value for a bootstrap-phase product.

If a region outage occurs, the recovery procedure is:

1. Deploy infrastructure to DR region (us-west-2) from Terraform
2. Restore DynamoDB redirect table from PITR in DR region
3. Restore Aurora cluster from latest backup in DR region
4. Replicate S3 analytics bucket content
5. Update DNS (Route 53 / customer CNAMEs) to point to DR region endpoints
6. Validate: redirects work, management dashboard loads, analytics queries return data

Estimated total recovery time: 4 hours.

For post-MVP, evaluate Aurora Global Database for near-zero-RPO control plane failover and DynamoDB global tables for multi-region active-active redirects.

### What Is Backed Up

| Component | Backed Up? | Mechanism |
|---|---|---|
| Redirect link data (DynamoDB) | Yes | PITR (35-day) |
| Control plane data (Postgres) | Yes | Automated backups (35-day prod, 7-day non-prod) |
| Analytics raw events (S3) | Yes | Cross-region replication |
| Lambda function code | Yes | Terraform (IaC), container images in ECR |
| API Gateway configuration | Yes | Terraform (IaC) |
| CloudFront configuration | Yes | Terraform (IaC) |
| DNS / Route 53 records | Yes | Terraform (IaC) |
| IAM roles and policies | Yes | Terraform (IaC) |
| X-Ray sampling rules | Yes | Terraform (IaC) |
| ACM certificates | Partial | Terraform creates; re-validation required on DR failover |

### What Is NOT Backed Up

| Component | Why Not | Mitigation |
|---|---|---|
| In-flight click events (< 15 min) | Pipeline latency window | Acceptable loss; analytics estimates within sampling tolerance |
| API Gateway access logs | Not critical for recovery | Can be re-enabled post-recovery if needed |
| CloudFront cache (edge content) | Ephemeral by design | Rebuilt on first request after recovery |
| Lambda cold-start warmed containers | Ephemeral by design | N/A |
| Stripe webhook deliveries (in-flight) | Stripe retries for 3 days | Webhook events are replayed by Stripe automatically |

## Constraints

- **DynamoDB PITR cost**: PITR adds ~20% to table storage cost. For the redirect table at scale (millions of links at ~2 KB each), this is estimated at < $10/month — acceptable for the durability benefit.
- **Aurora backup retention cost**: Automated backups up to 35 days are included in Aurora storage cost (no additional charge beyond storage). Restoring creates a new cluster, which incurs compute cost during the restore window (~$0.50/hour for minimum ACU).
- **S3 cross-region replication cost**: Source-to-destination data transfer at $0.02/GB. For 100 GB/month of analytics data, this is ~$2/month. Storage in the DR region doubles storage cost — $0.023/GB-month × 2.
- **Terraform state bucket MUST reside outside the infrastructure it manages**: If the Terraform state bucket is defined in the same Terraform configuration it manages, a `terraform destroy` or accidental deletion of the state bucket would make recovery impossible. The state bucket is created manually once per AWS account.
- **DR region capacity limits**: Lambda default concurrency limits apply per region. Ensure service quota increases are requested for the DR region ahead of time (or documented as part of the recovery procedure).
- **Customer DNS propagation**: After failover, customer CNAME records must point to new CloudFront endpoints. DNS TTLs determine how quickly traffic shifts. Typical propagation: 5-60 minutes.

## Decisions

1. **PITR over on-demand backups for DynamoDB** — PITR is continuous (no backup window), requires zero operational effort, and allows restore to any second in the 35-day window. On-demand backups require scheduling, have a coarser granularity (backup window), and cost the same per GB stored.
2. **Cross-region S3 replication over same-region versioning alone** — S3 versioning protects against accidental deletion within a region but does not protect against region-wide S3 failures. Cross-region replication provides geographic separation. The cost (~$2/month for analytics data volume) is justified.
3. **35-day retention for prod, 7-day for non-prod** — Production data needs a full month of recovery window for compliance and operational safety. Staging and sandbox environments don't need the same level of protection; 7 days is sufficient for debugging and recovery testing.
4. **Terraform-only recovery over manual recovery playbooks** — All infrastructure is defined in Terraform. Recovery means `terraform apply` in the DR region. Manual playbooks for individual resources create drift risk. The DR runbook references Terraform commands, not console click-paths.
5. **No multi-region active-active for MVP** — The cost and complexity of DynamoDB global tables + Aurora Global Database are not justified for a bootstrap-phase product. A documented cold-failover procedure with a 4-hour RTO is acceptable.
6. **DR region: us-west-2 (Oregon)** — Geographically distant from us-east-1 (primary), lower cost than us-west-1, supports all required AWS services, and is the standard DR region choice for us-east-1 workloads.

## Trade-offs

| Trade-off | Detail |
|---|---|
| **PITR cost vs durability** | PITR adds ~20% to table storage cost. For the redirect table, this is the difference between $0.25/GB-month and $0.30/GB-month. At 10 GB, this is $0.50/month — negligible for the value of continuous backup. |
| **Regional failover recovery time vs hot standby cost** | A 4-hour RTO during a regional outage (rare) is acceptable for an MVP. Running hot standby in us-west-2 would cost at minimum the Aurora Serverless v2 floor (~$30/month for minimum ACU) + DynamoDB global table replica write costs — at least $50-100/month for a scenario that may never occur. |
| **S3 replication lag vs cross-region durability** | S3 replication is asynchronous with typical 15-minute lag. In a region-loss scenario, up to 15 minutes of analytics data is unrecoverable. For billing purposes, this gap can be approximated from CloudFront access logs (which are stored separately in the primary region's S3). |
| **Terraform state as IaC dependency** | If the Terraform state bucket is lost AND the local state is unavailable, infrastructure cannot be recovered from Terraform without rebuilding state (`terraform import`). Mitigation: state bucket has versioning + same-region replication. Document the state bootstrap procedure. |
| **ACM certificate re-validation on failover** | ACM certificates are regional resources. Failing over to us-west-2 requires re-issuing certificates for customer domains. CloudFront certificates must be in us-east-1 regardless (CloudFront requirement), so the DR region CloudFront distribution can use the same certificate if it was issued in us-east-1. |

## Evolution

- **Post-MVP: Aurora Global Database** — If control plane availability becomes critical (paying customers cannot manage links during a region outage), add Aurora Global Database for cross-region read replicas with < 1-second replication lag and < 1-minute failover. This adds ~$100-200/month in compute and data transfer costs.
- **Post-MVP: DynamoDB Global Tables** — If redirect availability across regions is required (users in APAC/EMEA need local read latency, or region failover must be seamless), convert the redirect table to a DynamoDB global table. This adds write replication costs (~$0.02 per million replicated writes) but enables multi-region active-active.
- **Post-MVP: Automated DR testing** — Schedule a Lambda function that performs a dry-run restore (restore to new table, verify schema, delete) in the DR region monthly. This validates PITR is working and the restore procedure is correct, without manual effort.
- **If analytics retention grows beyond months**: Add S3 Intelligent-Tiering or Glacier lifecycle policies for older analytics data before replication. Replicating archived data that is rarely accessed is not cost-effective.
- **If customer SLAs require sub-hour RTO**: Pre-provision the DR region infrastructure (apply Terraform to us-west-2 in advance, keeping ACU minimums). Keep the infrastructure warm — deploy Lambda code, keep Aurora paused (scale to 0 ACU), keep DynamoDB table empty. On failover: restore data into pre-existing resources. This cuts RTO from 4 hours to < 1 hour.

## Related ADRs

- [ADR-002: DynamoDB for Redirect Lookups](ADR-002-dynamodb-redirect-lookups.md) — DynamoDB single-table is the redirect hot path; PITR covers it.
- [ADR-003: Serverless-First Architecture](ADR-003-serverless-first-architecture.md) — Stateless compute makes DR simpler; only data stores need recovery.
- [ADR-006: Postgres for Control Plane](ADR-006-postgres-control-plane.md) — Aurora Serverless v2 automated backups cover the control plane.
- [ADR-007: CloudFront Real-Time Logs for Analytics](ADR-007-cloudfront-real-time-logs-analytics.md) — S3 analytics data lake; cross-region replication covers it.
- [ADR-010: DynamoDB Single-Table Design](ADR-010-dynamodb-single-table-design.md) — The table schema; restore validates this schema.
