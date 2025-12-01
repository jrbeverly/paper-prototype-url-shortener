# SNS notifications for backup and restore job failures.
#
# Backup vault events (BACKUP_JOB_FAILED, RESTORE_JOB_FAILED) are forwarded
# to this topic via aws_backup_vault_notifications (backup.tf).
#
# The automated restore test Lambda (restore-test.tf) also publishes test
# results to this topic when failures or anomalies are detected.
#
# Email subscription is a placeholder — configure the endpoint_address in
# a .tfvars file before deploying to production.

resource "aws_sns_topic" "backup_alerts" {
  name              = "backup-alerts-${var.environment}"
  kms_master_key_id = var.environment == "prod" ? aws_kms_key.backup_vault[0].arn : null

  tags = local.common_tags
}

# ── Email subscription (placeholder) ──────────────────────────────────────

resource "aws_sns_topic_subscription" "backup_alerts_email" {
  count = var.environment == "prod" ? 1 : 0

  topic_arn = aws_sns_topic.backup_alerts.arn
  protocol  = "email"
  endpoint  = var.backup_notification_email
}

# ── Backup notification email variable ────────────────────────────────────

variable "backup_notification_email" {
  description = "Email address for backup job failure notifications (production only)."
  type        = string
  default     = "noreply@example.com" # Override in prod.tfvars
  sensitive   = true
}
