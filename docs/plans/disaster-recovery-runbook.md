# Disaster Recovery Runbook

**Version:** 1.1
**Last Updated:** 2026-06-02
**Owner:** Platform Engineering

---

## Overview

This runbook describes step-by-step procedures for recovering the platform from a total infrastructure loss in the primary region (us-east-1). It covers four recovery scenarios, ordered by increasing severity.

Recovery objectives (from ADR-013):
- **RPO:** 5 minutes (critical data), 15 minutes (analytics)
- **RTO:** 4 hours (full regional outage)

### Recovery Scenarios

| Scenario | Trigger | RTO | Procedure |
|---|---|---|---|
| S1 | Accidental DynamoDB data deletion | 1 hour | Restore from PITR |
| S2 | Aurora Postgres data loss or corruption | 1 hour | Restore from automated backup |
| S3 | Single service outage (Lambda/API Gateway misconfiguration) | 30 min | Terraform apply to fix configuration |
| S4 | Full regional outage (us-east-1 unavailable) | 4 hours | Cold failover to us-west-2 |

### Prerequisites

- AWS CLI installed and configured with credentials for both us-east-1 and us-west-2
- Terraform >= 1.6 installed
- Access to the Terraform state S3 bucket and DynamoDB lock table
- This runbook (stored outside the primary region — GitHub repository or printed copy)
- DR region (us-west-2) service quota increases requested for Lambda, API Gateway, CloudFront

---

## Scenario S1: DynamoDB Data Recovery

### When to Use

- Accidental deletion of link records
- Data corruption in the redirect table
- Buggy migration or bulk operation that corrupted data

### Procedure

```bash
# 1. Identify the restore timestamp (UTC, ISO 8601 format).
#    PITR records every change; choose a timestamp just before the incident.
RESTORE_TIMESTAMP="2026-06-01T10:00:00Z"
ENVIRONMENT="prod"

# 2. Restore to a new table.
aws dynamodb restore-table-to-point-in-time \
  --source-table-name "redirects-${ENVIRONMENT}" \
  --target-table-name "redirects-${ENVIRONMENT}-restored-$(date +%Y%m%d-%H%M%S)" \
  --restore-date-time "$RESTORE_TIMESTAMP" \
  --region us-east-1

# 3. Wait for the restore to complete (typically 30-120 minutes depending on table size).
aws dynamodb wait table-exists \
  --table-name "redirects-${ENVIRONMENT}-restored-*" \
  --region us-east-1

# 4. Verify the restored table.
#    - Check item count matches expectations.
#    - Spot-check known links resolve correctly.
aws dynamodb scan \
  --table-name "redirects-${ENVIRONMENT}-restored-*" \
  --select COUNT \
  --region us-east-1

# 5. Update the RedirectService Lambda environment variable to point to the restored table.
#    (Or update Parameter Store / AppConfig if using dynamic configuration.)
NEW_TABLE_NAME="redirects-${ENVIRONMENT}-restored-*"
aws lambda update-function-configuration \
  --function-name "redirect-service-${ENVIRONMENT}" \
  --environment "Variables={TABLE_NAME=${NEW_TABLE_NAME}}" \
  --region us-east-1

# 6. Validate redirects work.
curl -I "https://go.example.com/test-slug"

# 7. Once validated, delete the corrupted table (if still exists) and rename.
#    DynamoDB does not support rename; keep the restored table as the new primary.
#    Update Terraform to manage the new table name.

# 8. Clean up the old table after confirming no data loss.
aws dynamodb delete-table \
  --table-name "redirects-${ENVIRONMENT}" \
  --region us-east-1
```

### Validation Checklist

- [ ] Restored table item count matches expected (within the restore window)
- [ ] Spot-check 10 known links resolve correctly
- [ ] New link creation works (write path)
- [ ] Existing links redirect correctly (read path)
- [ ] Management dashboard lists all expected links

---

## Scenario S2: Aurora Postgres Recovery

### When to Use

- Accidental schema migration (dropped table, wrong column type)
- Data deletion without WHERE clause
- Data corruption detected by application errors

### Procedure

```bash
# 1. Identify the restore timestamp.
#    Aurora automated backups run every 5 minutes; transaction logs enable
#    point-in-time recovery to any second within the retention window.
ENVIRONMENT="prod"
CLUSTER_ID="control-plane-${ENVIRONMENT}"
RESTORE_TIMESTAMP="2026-06-01T09:55:00Z"

# 2. Restore the cluster to a point in time.
aws rds restore-db-cluster-to-point-in-time \
  --source-db-cluster-identifier "$CLUSTER_ID" \
  --db-cluster-identifier "${CLUSTER_ID}-restored-$(date +%Y%m%d-%H%M%S)" \
  --restore-to-time "$RESTORE_TIMESTAMP" \
  --db-subnet-group-name "control-plane-subnet" \
  --vpc-security-group-ids "sg-xxxx" \
  --region us-east-1

# 3. Create a DB instance in the restored cluster.
aws rds create-db-instance \
  --db-instance-identifier "${CLUSTER_ID}-restored-instance" \
  --db-cluster-identifier "${CLUSTER_ID}-restored-*" \
  --db-instance-class db.serverless \
  --engine aurora-postgresql \
  --region us-east-1

# 4. Wait for the instance to become available (5-15 minutes).
aws rds wait db-instance-available \
  --db-instance-identifier "${CLUSTER_ID}-restored-instance" \
  --region us-east-1

# 5. Get the restored cluster endpoint.
aws rds describe-db-clusters \
  --db-cluster-identifier "${CLUSTER_ID}-restored-*" \
  --query "DBClusters[0].Endpoint" \
  --region us-east-1

# 6. Update the ControlPlane API connection string.
#    Update the environment variable or secrets manager entry:
aws secretsmanager update-secret \
  --secret-id "control-plane-db-${ENVIRONMENT}" \
  --secret-string "{\"host\":\"<restored-endpoint>\",\"database\":\"controlplane\",\"username\":\"app\"}" \
  --region us-east-1

# 7. Restart the ControlPlane Lambda (forces cold start with new connection string).
aws lambda update-function-configuration \
  --function-name "control-plane-api-${ENVIRONMENT}" \
  --environment "Variables={DB_SECRET_ARN=control-plane-db-${ENVIRONMENT}}" \
  --region us-east-1

# 8. Run a smoke test.
curl "https://api.short.io/health" && echo "ControlPlane healthy"

# 9. Once validated, delete the corrupted cluster.
aws rds delete-db-cluster \
  --db-cluster-identifier "$CLUSTER_ID" \
  --skip-final-snapshot \
  --region us-east-1
```

### Validation Checklist

- [ ] ControlPlane health endpoint returns 200
- [ ] Tenant list endpoint returns expected tenants
- [ ] Link creation and retrieval works
- [ ] Stripe webhook processing works (create test webhook)
- [ ] DNS verification endpoint works

---

## Scenario S3: Single Service Recovery (Configuration Drift)

### When to Use

- Accidental Lambda configuration change
- API Gateway stage deleted
- IAM policy modified

### Procedure

```bash
# 1. Check what changed.
cd env/url-shortener
terraform plan

# 2. Review the plan output for unexpected changes.
#    Terraform shows what resources would be modified.

# 3. Apply to restore desired state.
terraform apply

# 4. If Terraform state is also corrupted, see S4 procedure.
```

---

## Scenario S4: Full Regional Outage — Cold Failover to us-west-2

### When to Use

- AWS us-east-1 is experiencing a region-wide outage
- Primary region infrastructure has been destroyed (accidental `terraform destroy`)
- Security incident requires immediate cutover to clean infrastructure

### Pre-Failover Checklist

Before starting, confirm:
- [ ] us-east-1 is genuinely unavailable (not a transient error)
- [ ] You have AWS console access (not just CLI, in case CLI is misconfigured)
- [ ] This runbook is accessible (stored in GitHub, not in the affected region)

### Phase 1: Infrastructure Bootstrap (Target: 30 minutes)

```bash
# 1. Clone repository if not already available.
git clone <repo-url> /tmp/recovery
cd /tmp/recovery

# 2. Initialize Terraform with DR region configuration.
#    The Terraform state bucket is in us-east-1; if us-east-1 S3 is down,
#    use the local state backup or start from a state import.
cd env/url-shortener

# If the state bucket is accessible:
terraform init

# If the state bucket is also in the affected region:
# Use the state file from the git repository backup (committed as encrypted artifact)
# or reconstruct state via `terraform import`.
terraform init -backend=false
terraform apply -target=aws_dynamodb_table.redirects -var="aws_region=us-west-2" -var="environment=prod"
```

```bash
# 3. Apply the full infrastructure to us-west-2.
#    Override the primary region variable to deploy to DR region.
terraform apply \
  -var="aws_region=us-west-2" \
  -var="environment=prod" \
  -var-file="prod.tfvars" 2>&1 | tee recovery-apply.log

# 4. Verify core infrastructure exists.
terraform output
```

### Phase 2: Data Restore (Target: 90 minutes)

**DynamoDB — Restore redirect table from PITR:**

```bash
# PITR restores to any region; specify us-west-2 as the target.
RESTORE_TIMESTAMP="<timestamp-before-outage>"

aws dynamodb restore-table-to-point-in-time \
  --source-table-arn "arn:aws:dynamodb:us-east-1:<account>:table/redirects-prod" \
  --target-table-name "redirects-prod" \
  --restore-date-time "$RESTORE_TIMESTAMP" \
  --region us-west-2

# Monitor restore progress.
aws dynamodb wait table-exists \
  --table-name "redirects-prod" \
  --region us-west-2

# Verify item count.
aws dynamodb scan \
  --table-name "redirects-prod" \
  --select COUNT \
  --region us-west-2
```

**Aurora Postgres — Restore from cross-region snapshot:**

```bash
# Aurora automated backups can be copied to DR region.
# Find the latest automated snapshot.
LATEST_SNAPSHOT=$(aws rds describe-db-cluster-snapshots \
  --db-cluster-identifier "control-plane-prod" \
  --snapshot-type automated \
  --query "reverse(sort_by(DBSnapshots, &SnapshotCreateTime))[0].DBSnapshotIdentifier" \
  --output text \
  --region us-east-1)

# Copy the snapshot to us-west-2 (if cross-region copy wasn't pre-configured).
aws rds copy-db-cluster-snapshot \
  --source-db-cluster-snapshot-identifier "$LATEST_SNAPSHOT" \
  --target-db-cluster-snapshot-identifier "control-plane-prod-dr-snapshot" \
  --source-region us-east-1 \
  --region us-west-2

# Wait for copy to complete.
aws rds wait db-cluster-snapshot-available \
  --db-cluster-snapshot-identifier "control-plane-prod-dr-snapshot" \
  --region us-west-2

# Restore cluster from the copied snapshot.
aws rds restore-db-cluster-from-snapshot \
  --db-cluster-identifier "control-plane-prod" \
  --snapshot-identifier "control-plane-prod-dr-snapshot" \
  --engine aurora-postgresql \
  --db-subnet-group-name "control-plane-subnet" \
  --vpc-security-group-ids "sg-xxxx" \
  --region us-west-2

# Create instance in restored cluster.
aws rds create-db-instance \
  --db-instance-identifier "control-plane-prod-instance" \
  --db-cluster-identifier "control-plane-prod" \
  --db-instance-class db.serverless \
  --engine aurora-postgresql \
  --region us-west-2
```

### Phase 3: Application Configuration (Target: 30 minutes)

```bash
# 1. Update application configuration.
#    RedirectService: point to restored DynamoDB table.
aws lambda update-function-configuration \
  --function-name "redirect-service-prod" \
  --environment "Variables={TABLE_NAME=redirects-prod}" \
  --region us-west-2

# 2. ControlPlane: point to restored Aurora cluster.
AURORA_ENDPOINT=$(aws rds describe-db-clusters \
  --db-cluster-identifier "control-plane-prod" \
  --query "DBClusters[0].Endpoint" \
  --output text \
  --region us-west-2)

aws secretsmanager update-secret \
  --secret-id "control-plane-db-prod" \
  --secret-string "{\"host\":\"${AURORA_ENDPOINT}\",\"database\":\"controlplane\",\"username\":\"app\"}" \
  --region us-west-2

# 3. Enable Lambda environment variables for the ControlPlane.
aws lambda update-function-configuration \
  --function-name "control-plane-api-prod" \
  --environment "Variables={DB_SECRET_ARN=control-plane-db-prod}" \
  --region us-west-2

# 4. Deploy API Gateway stage (if separate from Lambda function URL).
#    Terraform should have handled this in Phase 1.
```

### Phase 4: DNS Cutover (Target: 30 minutes)

```bash
# 1. Get the new CloudFront distribution domain name.
NEW_CF_DOMAIN=$(aws cloudfront get-distribution \
  --id "<distribution-id>" \
  --query "Distribution.DomainName" \
  --output text \
  --region us-west-2)

# 2. Update Route 53 ALIAS records to point to the new CloudFront distribution.
#    (Managed by Terraform — reapply with updated values.)
#    Customer CNAME records (go.customer.com → CloudFront) need to be updated
#    if the CloudFront endpoint changes.

# 3. If using CloudFront SaaS Manager:
#    Update the routing endpoint DNS to point to us-west-2 CloudFront distribution.

# 4. Monitor DNS propagation.
dig go.customer.com CNAME +short
# Wait for TTL to expire and records to propagate.
```

### Phase 5: Validation (Target: 30 minutes)

```bash
# 1. Health checks.
curl -s "https://api.short.io/health" | jq .
# Expected: {"status":"healthy","dependencies":{"dynamodb":"healthy","postgres":"healthy"}}

# 2. Redirect test.
curl -sI "https://go.customer.com/test-slug" | head -5
# Expected: HTTP/2 302 with Location header

# 3. Management API test.
curl -s "https://api.short.io/api/v1/tenants" \
  -H "Authorization: Bearer <token>" | jq .

# 4. Analytics query test.
#    Verify recent click data is queryable from the DR region S3 bucket.

# 5. Stripe webhook test.
#    Send a test webhook from Stripe dashboard to the new API endpoint.
```

### Post-Recovery

```bash
# 1. Monitor error rates and latency in CloudWatch for 30 minutes.
# 2. Verify analytics data is flowing to the DR S3 bucket.
# 3. Configure cross-region replication FROM us-west-2 back to us-east-1
#    (or to a new DR region) once the primary region is available again.
# 4. Document the incident: what failed, recovery timeline, lessons learned.
# 5. Schedule a DR test within 2 weeks to validate the new configuration.
```

---

## Automated Restore Testing

The platform includes an automated DynamoDB PITR restore test that runs quarterly (March, June, September, December 15th at 01:00 UTC). This validates that backups are working without requiring manual intervention for the most common recovery scenario (S1: DynamoDB data recovery).

### Infrastructure

| Resource | Name | Purpose |
|---|---|---|
| Lambda | `restore-test-{env}` | Performs PITR restore, schema validation, and cleanup |
| EventBridge Scheduler | `restore-test-quarterly-{env}` | Triggers the Lambda quarterly |
| SNS Topic | `backup-alerts-{env}` | Receives failure notifications |
| Backup Vault | `url-shortener-backup-{env}` | Stores scheduled on-demand backups |
| Backup Plan | `url-shortener-backup-plan-{env}` | Daily DynamoDB backup with retention rules |

### What the Restore Test Validates

1. DynamoDB PITR is enabled on the redirects table
2. A restore to point-in-time produces an ACTIVE table
3. The restored table has the expected schema (PK, SK, GSI1PK, GSI1SK + GSI1)
4. The restored table contains data (item count > 0)
5. Test tables are cleaned up after validation

### Monitoring

- **CloudWatch Logs:** `/aws/lambda/restore-test-{environment}`
- **CloudWatch Metrics:** Namespace `ShortIo/BackupRestoreTest` — `RestoreTestPassed`, `RestoreDuration`, `ItemCount`
- **SNS Notifications:** Failures publish to `backup-alerts-{environment}` with test details

### Interpreting Results

| Signal | Action |
|---|---|
| Lambda succeeds (Passed=true) | No action needed. PITR is working. |
| Lambda fails (Passed=false) | Investigate CloudWatch Logs. Check PITR status on the DynamoDB table. |
| Lambda timed out | Table may have grown beyond the restore window. Increase Lambda timeout or review table size. |
| No Lambda invocation | Check EventBridge Scheduler status and IAM role permissions. |

### Backup Retention Policies

| Store | Mechanism | Retention (prod) | Retention (non-prod) |
|---|---|---|---|
| DynamoDB (redirects) | PITR (continuous) | 35 days | Disabled (cost optimization) |
| DynamoDB (redirects) | AWS Backup (daily on-demand) | 35 days | 7 days |
| Aurora Postgres (control plane) | Automated backups + transaction logs | 35 days | 7 days |
| S3 (analytics) | Cross-region replication to us-west-2 | Source: lifecycle rules; DR copy: matches source | Disabled |
| S3 (Terraform state) | Versioning | All versions retained | All versions retained |

Documented in Terraform: `backup_retention_period`, `point_in_time_recovery.enabled`, `replication_configuration`. See `env/url-shortener/backup.tf`, `env/url-shortener/dynamodb.tf`, `env/url-shortener/rds-control-plane.tf`, `env/url-shortener/s3-backup.tf`.

### Code Location

- Lambda source: `src/BackupRestoreTest/BackupRestoreTest.Function/Function.cs`
- Tests: `test/BackupRestoreTest/BackupRestoreTest.Tests/FunctionTests.cs`
- Infrastructure: `env/url-shortener/backup.tf`, `env/url-shortener/restore-test.tf`, `env/url-shortener/sns-notifications.tf`, `env/url-shortener/rds-control-plane.tf`

---

## Terraform State Recovery

### If the Terraform State Bucket Is Lost

The Terraform state S3 bucket is the single most critical infrastructure resource. Without it, Terraform cannot manage any resources.

**Prevention:**
- State bucket has S3 versioning enabled (all versions preserved)
- State bucket has same-region replication to a separate bucket
- State file is backed up to the git repository as an encrypted artifact (monthly)

**Recovery procedure:**

```bash
# 1. If versioning is enabled, retrieve the previous version.
aws s3api get-object \
  --bucket "url-shortener-tfstate-prod" \
  --key "terraform.tfstate" \
  --version-id "<previous-version-id>" \
  terraform.tfstate

# 2. If the bucket itself is gone, recover from git backup.
git checkout recovery/terraform-state-backups
cp backups/terraform.tfstate .

# 3. Verify the state file.
terraform state list

# 4. If no state backup exists, reconstruct state via import.
#    For each resource in the infrastructure:
terraform import aws_dynamodb_table.redirects redirects-prod
terraform import aws_s3_bucket.analytics url-shortener-analytics-prod-<account-id>
# ... (repeat for all resources)
```

---

## Emergency Contacts and Escalation

### AWS Support

- AWS Health Dashboard: https://health.aws.amazon.com/
- AWS Support (Business plan required for < 1-hour response): https://support.aws.amazon.com/

### Internal

| Role | Contact | When |
|---|---|---|
| Platform Engineer | [primary contact] | Any DR scenario |
| Backup Engineer | [secondary contact] | Escalation if primary unavailable |

### External Dependencies

| Service | Impact if Down | Recovery |
|---|---|---|
| Stripe | Billing webhooks delayed | Stripe retries for 3 days |
| GitHub | Cannot clone repo for recovery | Keep a local backup of the repo and this runbook |
| AWS | Cannot recover | Wait for AWS to restore the region |
