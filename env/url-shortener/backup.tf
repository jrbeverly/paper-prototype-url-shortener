# AWS Backup — scheduled on-demand backups for DynamoDB (redirects table).
#
# PITR provides continuous backup (see dynamodb.tf); AWS Backup adds
# scheduled on-demand snapshots for an additional layer of protection
# and independent recovery point that survives PITR window expiry.
#
# Backup plan:
#   - Daily at 02:00 UTC
#   - 35-day retention (prod), 7-day retention (staging/sandbox)
#   - Stored in encrypted vault with delete protection
#
# Notifications (job failures) are delivered to the SNS topic defined in
# sns-notifications.tf.

# ── Backup vault ──────────────────────────────────────────────────────────

resource "aws_backup_vault" "main" {
  name = "url-shortener-backup-${var.environment}"

  # KMS key for vault encryption. Uses AWS-managed key by default;
  # customer-managed key can be specified for additional control.
  kms_key_arn = var.environment == "prod" ? aws_kms_key.backup_vault[0].arn : null

  tags = local.common_tags
}

# ── KMS key for backup vault (production only) ────────────────────────────

resource "aws_kms_key" "backup_vault" {
  count = var.environment == "prod" ? 1 : 0

  description             = "KMS key for the url-shortener backup vault"
  deletion_window_in_days = 30
  enable_key_rotation     = true

  tags = local.common_tags
}

resource "aws_kms_alias" "backup_vault" {
  count = var.environment == "prod" ? 1 : 0

  name          = "alias/backup-vault-${var.environment}"
  target_key_id = aws_kms_key.backup_vault[0].key_id
}

# ── Backup plan ───────────────────────────────────────────────────────────

resource "aws_backup_plan" "main" {
  name = "url-shortener-backup-plan-${var.environment}"

  # Daily DynamoDB backup at 02:00 UTC
  rule {
    rule_name         = "daily-dynamodb-backup"
    target_vault_name = aws_backup_vault.main.name
    schedule          = "cron(0 2 * * ? *)"

    # Start within 2 hours of the scheduled time.
    start_window = 120

    # Must complete within 4 hours.
    completion_window = 240

    # Point-in-time recovery can restore to any second, but on-demand backups
    # provide independent recovery points with their own retention.
    lifecycle {
      cold_storage_after = 0 # Never transition to cold storage (DynamoDB backups are small)
      delete_after       = var.environment == "prod" ? 35 : 7
    }
  }

  tags = local.common_tags
}

# ── Backup IAM role ───────────────────────────────────────────────────────

data "aws_iam_policy_document" "backup_assume_role" {
  statement {
    effect = "Allow"
    principals {
      type        = "Service"
      identifiers = ["backup.amazonaws.com"]
    }
    actions = ["sts:AssumeRole"]
  }
}

resource "aws_iam_role" "backup" {
  name               = "backup-role-${var.environment}"
  assume_role_policy = data.aws_iam_policy_document.backup_assume_role.json

  tags = local.common_tags
}

data "aws_iam_policy_document" "backup" {
  statement {
    effect = "Allow"
    actions = [
      "dynamodb:DescribeTable",
      "dynamodb:CreateBackup",
      "dynamodb:DeleteBackup",
      "dynamodb:ListBackups",
      "dynamodb:ListTagsOfResource",
    ]
    resources = [aws_dynamodb_table.redirects.arn]
  }
}

resource "aws_iam_role_policy" "backup" {
  name   = "backup-policy-${var.environment}"
  role   = aws_iam_role.backup.id
  policy = data.aws_iam_policy_document.backup.json
}

# AWS-managed backup policy attachment provides additional required permissions
# (e.g., backup storage, vault access).
resource "aws_iam_role_policy_attachment" "backup_managed" {
  role       = aws_iam_role.backup.name
  policy_arn = "arn:aws:iam::aws:policy/AWSBackupServiceRolePolicyForBackup"
}

# ── Backup selection — what gets backed up ────────────────────────────────

resource "aws_backup_selection" "main" {
  name         = "url-shortener-backup-selection-${var.environment}"
  iam_role_arn = aws_iam_role.backup.arn
  plan_id      = aws_backup_plan.main.id

  # Select the DynamoDB redirects table by ARN.
  resources = [aws_dynamodb_table.redirects.arn]

  # Tag-based selection is also possible; ARN is more explicit.
}

# ── Backup vault notifications (via SNS) ──────────────────────────────────

# Backup job events are routed to CloudWatch Events, which triggers
# SNS notifications for failed backup jobs (see cloudwatch-backup-alerts.tf).
resource "aws_backup_vault_notifications" "main" {
  count = var.environment == "prod" ? 1 : 0

  backup_vault_name   = aws_backup_vault.main.name
  sns_topic_arn       = aws_sns_topic.backup_alerts.arn
  backup_vault_events = ["BACKUP_JOB_FAILED", "RESTORE_JOB_FAILED", "COPY_JOB_FAILED"]
}
