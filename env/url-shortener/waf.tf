# AWS WAF Web ACL for CloudFront protection.
#
# CLOUDFRONT scope requires deployment in us-east-1. The default AWS provider
# in this workspace already targets us-east-1 (see main.tf), so no additional
# provider alias is needed.
#
# Rules (evaluated in priority order — lower number wins):
#   1  AWSManagedRulesAmazonIpReputationList  — known malicious IPs / botnets
#   2  AWSManagedRulesCommonRuleSet           — OWASP Top 10 core protections
#   3  AWSManagedRulesKnownBadInputsRuleSet   — log4j / Spring4Shell / SSRF probes
#   4  AWSManagedRulesSQLiRuleSet             — SQL injection patterns
#   5  GlobalRateLimit                        — per-IP flood protection
#   6  PerDomainRateLimit                     — per-IP-per-tenant-domain protection
#   7  BlockEmptyUserAgent                    — rejects requests missing a User-Agent
#  10  GeoBlock (conditional)                 — country-level blocking when configured
#
# Integrate with CloudFront by passing the waf_web_acl_arn output to the
# cloudfront-distribution module's web_acl_id variable.

# ── WAF log bucket ─────────────────────────────────────────────────────────
#
# WAF log bucket names must start with "aws-waf-logs-" (AWS requirement).
# Logs only BLOCK decisions (see aws_wafv2_web_acl_logging_configuration below)
# to keep storage costs proportional to threat activity, not traffic volume.

resource "aws_s3_bucket" "waf_logs" {
  bucket = "aws-waf-logs-url-shortener-${var.environment}-${data.aws_caller_identity.current.account_id}"

  tags = local.common_tags
}

resource "aws_s3_bucket_public_access_block" "waf_logs" {
  bucket = aws_s3_bucket.waf_logs.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "waf_logs" {
  bucket = aws_s3_bucket.waf_logs.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_lifecycle_configuration" "waf_logs" {
  bucket = aws_s3_bucket.waf_logs.id

  rule {
    id     = "expire-waf-logs"
    status = "Enabled"

    expiration {
      days = var.environment == "prod" ? 365 : 30
    }
  }
}

# Allow the WAF log delivery service to write blocked-request logs.
# Service principal conditions prevent cross-account abuse of the bucket.
data "aws_iam_policy_document" "waf_logs_bucket" {
  statement {
    sid    = "AWSLogDeliveryWrite"
    effect = "Allow"

    principals {
      type        = "Service"
      identifiers = ["delivery.logs.amazonaws.com"]
    }

    actions   = ["s3:PutObject"]
    resources = ["${aws_s3_bucket.waf_logs.arn}/AWSLogs/${data.aws_caller_identity.current.account_id}/*"]

    condition {
      test     = "StringEquals"
      variable = "s3:x-amz-acl"
      values   = ["bucket-owner-full-control"]
    }

    condition {
      test     = "StringEquals"
      variable = "aws:SourceAccount"
      values   = [data.aws_caller_identity.current.account_id]
    }
  }

  statement {
    sid    = "AWSLogDeliveryAclCheck"
    effect = "Allow"

    principals {
      type        = "Service"
      identifiers = ["delivery.logs.amazonaws.com"]
    }

    actions   = ["s3:GetBucketAcl"]
    resources = [aws_s3_bucket.waf_logs.arn]

    condition {
      test     = "StringEquals"
      variable = "aws:SourceAccount"
      values   = [data.aws_caller_identity.current.account_id]
    }
  }
}

resource "aws_s3_bucket_policy" "waf_logs" {
  bucket     = aws_s3_bucket.waf_logs.id
  policy     = data.aws_iam_policy_document.waf_logs_bucket.json
  depends_on = [aws_s3_bucket_public_access_block.waf_logs]
}

# ── WAF Web ACL ────────────────────────────────────────────────────────────

resource "aws_wafv2_web_acl" "cloudfront" {
  name  = "url-shortener-${var.environment}"
  scope = "CLOUDFRONT"

  default_action {
    allow {}
  }

  # ── Managed rule: IP reputation list ─────────────────────────────────────
  # Evaluated first: blocks known botnet IPs, command-and-control servers,
  # and Tor exit nodes before any further rule processing. Eliminates a large
  # share of attack traffic at the lowest possible cost per rule evaluation.
  rule {
    name     = "AWSManagedRulesAmazonIpReputationList"
    priority = 1

    override_action {
      none {}
    }

    statement {
      managed_rule_group_statement {
        name        = "AWSManagedRulesAmazonIpReputationList"
        vendor_name = "AWS"
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "AWSManagedRulesAmazonIpReputationList"
      sampled_requests_enabled   = true
    }
  }

  # ── Managed rule: Common Rule Set ────────────────────────────────────────
  # Protects against OWASP Top 10 categories: XSS, path traversal, RFI, LFI,
  # and other common web exploits. Applies to all origins behind the distribution.
  rule {
    name     = "AWSManagedRulesCommonRuleSet"
    priority = 2

    override_action {
      none {}
    }

    statement {
      managed_rule_group_statement {
        name        = "AWSManagedRulesCommonRuleSet"
        vendor_name = "AWS"
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "AWSManagedRulesCommonRuleSet"
      sampled_requests_enabled   = true
    }
  }

  # ── Managed rule: Known Bad Inputs ───────────────────────────────────────
  # Blocks requests containing log4j JNDI lookups, Spring4Shell payloads,
  # and EC2 instance metadata SSRF probing patterns.
  rule {
    name     = "AWSManagedRulesKnownBadInputsRuleSet"
    priority = 3

    override_action {
      none {}
    }

    statement {
      managed_rule_group_statement {
        name        = "AWSManagedRulesKnownBadInputsRuleSet"
        vendor_name = "AWS"
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "AWSManagedRulesKnownBadInputsRuleSet"
      sampled_requests_enabled   = true
    }
  }

  # ── Managed rule: SQL Injection ──────────────────────────────────────────
  # Inspects URI, query string, headers, and body for SQL injection patterns.
  # Relevant for control plane API endpoints backed by Aurora Serverless v2.
  rule {
    name     = "AWSManagedRulesSQLiRuleSet"
    priority = 4

    override_action {
      none {}
    }

    statement {
      managed_rule_group_statement {
        name        = "AWSManagedRulesSQLiRuleSet"
        vendor_name = "AWS"
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "AWSManagedRulesSQLiRuleSet"
      sampled_requests_enabled   = true
    }
  }

  # ── Custom rule: Global IP rate limit ────────────────────────────────────
  # Blocks source IPs that exceed the threshold in any 5-minute window.
  # Prevents redirect enumeration, link scanning, and flood attacks from
  # a single IP regardless of which tenant domains are targeted.
  rule {
    name     = "GlobalRateLimit"
    priority = 5

    action {
      block {}
    }

    statement {
      rate_based_statement {
        limit              = var.waf_rate_limit_global
        aggregate_key_type = "IP"
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "GlobalRateLimit"
      sampled_requests_enabled   = true
    }
  }

  # ── Custom rule: Per-domain rate limit ───────────────────────────────────
  # Aggregates by (source IP + Host header) so one IP cannot flood a specific
  # tenant domain even while staying under the global per-IP threshold when
  # spreading requests across the platform.
  rule {
    name     = "PerDomainRateLimit"
    priority = 6

    action {
      block {}
    }

    statement {
      rate_based_statement {
        limit              = var.waf_rate_limit_per_domain
        aggregate_key_type = "CUSTOM_KEYS"

        custom_key {
          ip {}
        }

        custom_key {
          header {
            name = "host"

            text_transformation {
              priority = 0
              type     = "LOWERCASE"
            }
          }
        }
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "PerDomainRateLimit"
      sampled_requests_enabled   = true
    }
  }

  # ── Custom rule: Empty User-Agent ────────────────────────────────────────
  # Rejects requests with a missing User-Agent header. All legitimate clients
  # (browsers, mobile apps, link-preview services, search crawlers) send one.
  # Automated abuse tools (scanners, credential stuffers) frequently omit it.
  rule {
    name     = "BlockEmptyUserAgent"
    priority = 7

    action {
      block {}
    }

    statement {
      size_constraint_statement {
        comparison_operator = "EQ"
        size                = 0

        field_to_match {
          single_header {
            name = "user-agent"
          }
        }

        text_transformation {
          priority = 0
          type     = "NONE"
        }
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "BlockEmptyUserAgent"
      sampled_requests_enabled   = true
    }
  }

  # ── Optional rule: Country geo-block ─────────────────────────────────────
  # Activated when waf_blocked_countries is set to a non-empty list.
  # Use for compliance requirements that mandate blocking specific jurisdictions.
  # CloudFront-level geo restrictions (in the cloudfront-distribution module)
  # are an alternative if per-path granularity is not needed.
  dynamic "rule" {
    for_each = length(var.waf_blocked_countries) > 0 ? [1] : []

    content {
      name     = "GeoBlock"
      priority = 10

      action {
        block {}
      }

      statement {
        geo_match_statement {
          country_codes = var.waf_blocked_countries
        }
      }

      visibility_config {
        cloudwatch_metrics_enabled = true
        metric_name                = "GeoBlock"
        sampled_requests_enabled   = true
      }
    }
  }

  visibility_config {
    cloudwatch_metrics_enabled = true
    metric_name                = "url-shortener-waf-${var.environment}"
    sampled_requests_enabled   = true
  }

  tags = local.common_tags
}

# ── WAF logging configuration ──────────────────────────────────────────────
#
# Only BLOCK decisions are retained. ALLOW traffic is captured at higher
# fidelity by the CloudFront real-time log pipeline (ADR-007), so logging
# allows here would be redundant and costly at redirect scale.

resource "aws_wafv2_web_acl_logging_configuration" "cloudfront" {
  log_destination_configs = [aws_s3_bucket.waf_logs.arn]
  resource_arn            = aws_wafv2_web_acl.cloudfront.arn

  depends_on = [aws_s3_bucket_policy.waf_logs]

  logging_filter {
    default_behavior = "DROP"

    filter {
      behavior    = "KEEP"
      requirement = "MEETS_ANY"

      condition {
        action_condition {
          action = "BLOCK"
        }
      }
    }
  }
}

# ── CloudWatch alarms ──────────────────────────────────────────────────────
#
# WAF CLOUDFRONT metrics are emitted in us-east-1 regardless of the
# originating edge location. The "Region" dimension must be "us-east-1".
#
# Alarms publish to the operational SNS topic (sns-notifications.tf) so
# WAF activity is visible alongside backup and restore failure notifications.

resource "aws_cloudwatch_metric_alarm" "waf_blocked_high" {
  alarm_name          = "waf-blocked-requests-high-${var.environment}"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "BlockedRequests"
  namespace           = "AWS/WAFV2"
  period              = 300
  statistic           = "Sum"
  threshold           = var.waf_alarm_blocked_threshold
  alarm_description   = "WAF is blocking an unusually high number of requests. Investigate for active attack patterns."
  treat_missing_data  = "notBreaching"

  dimensions = {
    WebACL = aws_wafv2_web_acl.cloudfront.name
    Region = "us-east-1"
    Rule   = "ALL"
  }

  alarm_actions = [aws_sns_topic.backup_alerts.arn]
  ok_actions    = [aws_sns_topic.backup_alerts.arn]

  tags = local.common_tags
}

resource "aws_cloudwatch_metric_alarm" "waf_rate_limit_triggered" {
  alarm_name          = "waf-rate-limit-triggered-${var.environment}"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "BlockedRequests"
  namespace           = "AWS/WAFV2"
  period              = 300
  statistic           = "Sum"
  threshold           = var.waf_alarm_rate_limit_threshold
  alarm_description   = "WAF rate limiting is actively blocking IPs. Possible brute-force or scraping attack in progress."
  treat_missing_data  = "notBreaching"

  dimensions = {
    WebACL = aws_wafv2_web_acl.cloudfront.name
    Region = "us-east-1"
    Rule   = "GlobalRateLimit"
  }

  alarm_actions = [aws_sns_topic.backup_alerts.arn]

  tags = local.common_tags
}

# ── Variables ──────────────────────────────────────────────────────────────

variable "waf_rate_limit_global" {
  description = "Maximum requests per source IP per 5-minute window before the IP is blocked globally. AWS WAFv2 minimum is 100."
  type        = number
  default     = 10000

  validation {
    condition     = var.waf_rate_limit_global >= 100
    error_message = "waf_rate_limit_global must be at least 100 (AWS WAFv2 minimum)."
  }
}

variable "waf_rate_limit_per_domain" {
  description = "Maximum requests per source IP per tenant domain per 5-minute window. AWS WAFv2 minimum is 100."
  type        = number
  default     = 2000

  validation {
    condition     = var.waf_rate_limit_per_domain >= 100
    error_message = "waf_rate_limit_per_domain must be at least 100 (AWS WAFv2 minimum)."
  }
}

variable "waf_blocked_countries" {
  description = "ISO 3166-1 alpha-2 country codes to block at the WAF layer. Empty list disables geo-blocking. Example: [\"RU\", \"CN\"]."
  type        = list(string)
  default     = []
}

variable "waf_alarm_blocked_threshold" {
  description = "WAF total blocked request count per 5-minute period that triggers the high-volume CloudWatch alarm."
  type        = number
  default     = 1000
}

variable "waf_alarm_rate_limit_threshold" {
  description = "WAF rate-limit block count per 5-minute period that triggers the rate-limit CloudWatch alarm."
  type        = number
  default     = 100
}

# ── Outputs ────────────────────────────────────────────────────────────────

output "waf_web_acl_arn" {
  description = "WAF Web ACL ARN. Pass to the cloudfront-distribution module's web_acl_id variable to associate WAF with the distribution."
  value       = aws_wafv2_web_acl.cloudfront.arn
}

output "waf_logs_bucket" {
  description = "S3 bucket name receiving WAF blocked-request logs."
  value       = aws_s3_bucket.waf_logs.bucket
}
