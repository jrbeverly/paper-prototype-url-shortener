# Analytics data lake S3 bucket with cross-region replication.
# Receives Parquet files from Kinesis Firehose per ADR-007.
#
# Cross-region replication provides geographic separation for analytics data.
# In a region outage, the replica bucket in us-west-2 contains all analytics
# data up to the replication lag window (~15 min per ADR-013).

# ── Primary region bucket (us-east-1) ───────────────────────────────────

resource "aws_s3_bucket" "analytics" {
  bucket = "url-shortener-analytics-${var.environment}-${data.aws_caller_identity.current.account_id}"

  tags = local.common_tags
}

resource "aws_s3_bucket_versioning" "analytics" {
  bucket = aws_s3_bucket.analytics.id

  versioning_configuration {
    status = "Enabled"
  }
}

# Encrypt analytics data at rest with SSE-S3 (AES-256).
# SSE-KMS adds per-object key management cost; SSE-S3 is sufficient for
# analytics data that does not contain PII beyond IP addresses.
resource "aws_s3_bucket_server_side_encryption_configuration" "analytics" {
  bucket = aws_s3_bucket.analytics.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

# Block public access — analytics data must never be publicly readable.
resource "aws_s3_bucket_public_access_block" "analytics" {
  bucket = aws_s3_bucket.analytics.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# ── DR region replica bucket (us-west-2) ────────────────────────────────

resource "aws_s3_bucket" "analytics_replica" {
  provider = aws.dr
  bucket   = "url-shortener-analytics-${var.environment}-${data.aws_caller_identity.current.account_id}-dr"

  tags = merge(local.common_tags, { "DR" = "true" })
}

resource "aws_s3_bucket_versioning" "analytics_replica" {
  provider = aws.dr
  bucket   = aws_s3_bucket.analytics_replica.id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "analytics_replica" {
  provider = aws.dr
  bucket   = aws_s3_bucket.analytics_replica.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_public_access_block" "analytics_replica" {
  provider = aws.dr
  bucket   = aws_s3_bucket.analytics_replica.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# ── Replication IAM role ────────────────────────────────────────────────

data "aws_iam_policy_document" "s3_replication_assume_role" {
  statement {
    effect = "Allow"
    principals {
      type        = "Service"
      identifiers = ["s3.amazonaws.com"]
    }
    actions = ["sts:AssumeRole"]
  }
}

resource "aws_iam_role" "s3_replication" {
  name               = "s3-replication-analytics-${var.environment}"
  assume_role_policy = data.aws_iam_policy_document.s3_replication_assume_role.json

  tags = local.common_tags
}

data "aws_iam_policy_document" "s3_replication" {
  statement {
    effect = "Allow"
    actions = [
      "s3:GetReplicationConfiguration",
      "s3:ListBucket",
    ]
    resources = [aws_s3_bucket.analytics.arn]
  }

  statement {
    effect = "Allow"
    actions = [
      "s3:GetObjectVersionForReplication",
      "s3:GetObjectVersionAcl",
      "s3:GetObjectVersionTagging",
    ]
    resources = ["${aws_s3_bucket.analytics.arn}/*"]
  }

  statement {
    effect = "Allow"
    actions = [
      "s3:ReplicateObject",
      "s3:ReplicateDelete",
      "s3:ReplicateTags",
    ]
    resources = ["${aws_s3_bucket.analytics_replica.arn}/*"]
  }
}

resource "aws_iam_role_policy" "s3_replication" {
  name   = "s3-replication-analytics-${var.environment}"
  role   = aws_iam_role.s3_replication.id
  policy = data.aws_iam_policy_document.s3_replication.json
}

# ── Replication configuration ───────────────────────────────────────────

resource "aws_s3_bucket_replication_configuration" "analytics" {
  depends_on = [aws_s3_bucket_versioning.analytics, aws_s3_bucket_versioning.analytics_replica]
  bucket     = aws_s3_bucket.analytics.id
  role       = aws_iam_role.s3_replication.arn

  rule {
    id     = "replicate-all-to-dr-region"
    status = var.environment == "prod" ? "Enabled" : "Disabled"

    destination {
      bucket        = aws_s3_bucket.analytics_replica.arn
      storage_class = "STANDARD"
    }
  }
}

# ── Account ID data source ──────────────────────────────────────────────

data "aws_caller_identity" "current" {}
