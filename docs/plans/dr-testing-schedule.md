# Disaster Recovery Testing Schedule

**Version:** 1.1
**Last Updated:** 2026-06-02
**Owner:** Platform Engineering

---

## Testing Cadence

DR tests are scheduled quarterly. Each test validates a specific recovery scenario from the [DR Runbook](disaster-recovery-runbook.md).

| Quarter | Month | Scenario | Focus |
|---|---|---|---|
| Q1 | March | S1: DynamoDB PITR restore | Validate redirect table can be restored from PITR |
| Q2 | June | S2: Aurora Postgres restore | Validate control plane database can be restored from automated backup |
| Q3 | September | S1 + S2: Combined data restore | Validate both data stores restore correctly and consistently |
| Q4 | December | S4: Full regional failover (dry run) | Validate cold failover to us-west-2 without cutting DNS |

### Automated Restore Test (DynamoDB PITR)

In addition to the manual quarterly tests above, an automated restore test runs on the **15th of March, June, September, and December at 01:00 UTC** (aligned with quarterly review dates). This avoids manual scheduling for the common S1 scenario and serves as a continuous validation that PITR is enabled and working.

**Infrastructure (Terraform, `env/url-shortener/restore-test.tf`):**
- **Lambda function:** `restore-test-{environment}` — performs the PITR restore, validates schema and item count, deletes the test table.
- **EventBridge Scheduler:** `restore-test-quarterly-{environment}` — triggers the Lambda on the quarterly cron.
- **SNS notifications:** Failures are published to `backup-alerts-{environment}` (see `sns-notifications.tf`).
- **IAM role:** `restore-test-lambda-{environment}` — grants `dynamodb:RestoreTableToPointInTime`, `dynamodb:DeleteTable`, and related permissions.

**What it validates:**
1. DynamoDB PITR is enabled and operational
2. Restored table has the expected schema (PK, SK, GSI1PK, GSI1SK + GSI1 index)
3. Restored table contains non-zero data (data completeness check)
4. Restore completes within the Lambda timeout (10 minutes)
5. Test table is cleaned up after validation

**Results:** Success/failure is recorded in CloudWatch Logs (`/aws/lambda/restore-test-{environment}`) with EMF metrics in the `ShortIo/BackupRestoreTest` namespace for CloudWatch dashboards.

**Source code:** `src/BackupRestoreTest/BackupRestoreTest.Function/Function.cs`

### Additional Triggers

A DR test is also required after:
- Major schema changes to DynamoDB or Postgres
- Terraform provider version upgrades
- New AWS service adoption (e.g., adding ElastiCache)
- Any production incident that touched backup/restore infrastructure

---

## Quarterly Test Procedure

### Pre-Test (1 day before)

- [ ] Notify team: DR test scheduled for [date/time], expected duration 2 hours
- [ ] Verify AWS credentials are valid for both us-east-1 and us-west-2
- [ ] Verify Terraform version matches the version used in CI
- [ ] Verify the runbook is accessible outside the primary region
- [ ] Take a manual snapshot of critical data for rollback safety

### Test Execution

**Q1 Test: DynamoDB PITR Restore**

```bash
# 1. Note the current time for restore point.
echo "Restore point: $(date -u +%Y-%m-%dT%H:%M:%SZ)"

# 2. Create a test link after the restore point.
curl -s -X POST "https://api.short.io/api/v1/tenants/<test-tenant>/links" \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"domain_id":"<domain>","destination_url":"https://example.com/dr-test","slug":"dr-test-<timestamp>"}'

# 3. Restore the redirect table to the noted timestamp in the SAME region.
RESTORE_TIMESTAMP="<noted-timestamp>"

aws dynamodb restore-table-to-point-in-time \
  --source-table-name "redirects-prod" \
  --target-table-name "redirects-prod-dr-test-$(date +%Y%m%d-%H%M%S)" \
  --restore-date-time "$RESTORE_TIMESTAMP" \
  --region us-east-1

# 4. Wait for restore to complete.
aws dynamodb wait table-exists \
  --table-name "redirects-prod-dr-test-*" \
  --region us-east-1

# 5. Validate the test table.
#    - Item count should be one less than current (test link not in snapshot).
aws dynamodb scan \
  --table-name "redirects-prod-dr-test-*" \
  --select COUNT \
  --region us-east-1

# 6. Spot-check: the test link created in step 2 should NOT exist in the restored table.
aws dynamodb get-item \
  --table-name "redirects-prod-dr-test-*" \
  --key '{"PK":{"S":"HOST#go.example.com#SLUG#dr-test-<timestamp>"},"SK":{"S":"CONFIG"}}' \
  --region us-east-1
# Expected: item not found

# 7. Spot-check: a link created before the restore point SHOULD exist.
#    (Verify a known production link exists in the restored table.)

# 8. Clean up the test table.
aws dynamodb delete-table \
  --table-name "redirects-prod-dr-test-*" \
  --region us-east-1
```

**Q2 Test: Aurora Postgres Restore**

```bash
# 1. Restore the cluster to a point-in-time (5 minutes ago).
aws rds restore-db-cluster-to-point-in-time \
  --source-db-cluster-identifier "control-plane-prod" \
  --db-cluster-identifier "control-plane-prod-dr-test-$(date +%m%d-%H%M)" \
  --restore-to-time "$(date -u -d '5 minutes ago' +%Y-%m-%dT%H:%M:%SZ)" \
  --db-subnet-group-name "control-plane-subnet" \
  --vpc-security-group-ids "sg-xxxx" \
  --region us-east-1

# 2. Create DB instance in the test cluster.
aws rds create-db-instance \
  --db-instance-identifier "control-plane-prod-dr-test-instance" \
  --db-cluster-identifier "control-plane-prod-dr-test-*" \
  --db-instance-class db.serverless \
  --engine aurora-postgresql \
  --region us-east-1

# 3. Wait for instance to be available.
aws rds wait db-instance-available \
  --db-instance-identifier "control-plane-prod-dr-test-instance" \
  --region us-east-1

# 4. Verify the restored cluster is queryable.
RESTORED_ENDPOINT=$(aws rds describe-db-clusters \
  --db-cluster-identifier "control-plane-prod-dr-test-*" \
  --query "DBClusters[0].Endpoint" \
  --output text \
  --region us-east-1)

PGPASSWORD="<password>" psql \
  -h "$RESTORED_ENDPOINT" \
  -U app \
  -d controlplane \
  -c "SELECT count(*) FROM tenants;"

# 5. Verify data integrity: count tables match production.
#    Compare row counts for critical tables: tenants, domains, links, api_keys.

# 6. Clean up.
aws rds delete-db-instance \
  --db-instance-identifier "control-plane-prod-dr-test-instance" \
  --skip-final-snapshot \
  --region us-east-1

aws rds delete-db-cluster \
  --db-cluster-identifier "control-plane-prod-dr-test-*" \
  --skip-final-snapshot \
  --region us-east-1
```

**Q3 Test: Combined Data Restore**

Execute Q1 and Q2 tests in sequence, then verify cross-store consistency:

- [ ] DynamoDB restore passes (Q1)
- [ ] Postgres restore passes (Q2)
- [ ] For 10 randomly selected link IDs in Postgres, verify the corresponding DynamoDB record exists in the restored table
- [ ] Verify domain records in Postgres match the domains referenced by links in DynamoDB

**Q4 Test: Full Regional Failover (Dry Run)**

Execute the S4 procedure from the runbook, but do NOT cut DNS. Validate:

- [ ] Infrastructure deploys to us-west-2 successfully (`terraform apply`)
- [ ] DynamoDB restores from cross-region PITR
- [ ] Aurora restores from cross-region snapshot copy
- [ ] RedirectService Lambda returns correct redirects (test via Lambda invoke, not DNS)
- [ ] ControlPlane API returns correct data (test via API Gateway endpoint, not DNS)
- [ ] All validation steps from the S4 procedure pass
- [ ] Clean up DR region resources (do not leave idle infrastructure running)

### Post-Test (within 1 week)

Write a DR test report covering:
1. Test date, scenario tested, participants
2. RTO achieved vs target (e.g., "DynamoDB restore took 47 minutes vs 60 min target")
3. Any failures or unexpected behavior
4. Runbook accuracy — any steps that were incomplete or wrong
5. Actions required before next test

Store the report in `docs/plans/dr-test-reports/DR-TEST-YYYY-QX.md`.

---

## Success Criteria

A DR test is considered **passing** when:

| Scenario | Criteria |
|---|---|
| S1 (DynamoDB) | Restored table is queryable, item count within 1% of expected, spot-checks pass |
| S2 (Postgres) | Restored cluster is connectable, row counts match production, spot-checks pass |
| S3 (Service) | `terraform apply` restores expected state, service health checks pass |
| S4 (Full failover) | All S1+S2+S3 criteria pass in the DR region; API responds correctly to test requests |

If a test fails, schedule a remediation within 2 weeks and re-test within 1 month. Document the failure and root cause in the DR test report.

---

## Test Schedule (2026-2027)

| Date | Quarter | Scenario | Status |
|---|---|---|---|
| 2026-09-15 | Q3 2026 | Combined DynamoDB + Postgres restore | Scheduled |
| 2026-12-15 | Q4 2026 | Full regional failover (dry run) | Scheduled |
| 2027-03-15 | Q1 2027 | DynamoDB PITR restore | Scheduled |
| 2027-06-15 | Q2 2027 | Postgres restore | Scheduled |
| 2027-09-15 | Q3 2027 | Combined data restore | Scheduled |
| 2027-12-15 | Q4 2027 | Full regional failover (dry run) | Scheduled |

**First test:** Q3 2026 (September 15) — allows 3 months for infrastructure to stabilize. If the project reaches production readiness earlier, move the first test forward.

---

## Test Environment

DR tests execute in the **production AWS account** but create isolated test resources:

- DynamoDB: test tables with `-dr-test-<timestamp>` suffix, deleted after test
- Aurora: test clusters with `-dr-test-<timestamp>` suffix, deleted after test
- Lambda/API Gateway: no test resources needed (can invoke directly)
- S3: no test bucket needed (replication is tested during Q4 failover)

Test resources MUST be cleaned up within 24 hours of test completion to avoid cost accumulation. The runbook cleanup steps enforce this.
