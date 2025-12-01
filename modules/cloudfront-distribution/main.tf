terraform {
  required_version = ">= 1.6, < 2.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = ">= 5.0"
    }
  }
}

# ── CloudFront Distribution ──────────────────────────────────────────────────

resource "aws_cloudfront_distribution" "main" {
  enabled             = var.enabled
  price_class         = var.price_class
  aliases             = length(var.aliases) > 0 ? var.aliases : null
  default_root_object = var.default_root_object
  comment             = var.name
  web_acl_id          = var.web_acl_id

  # ── Origins ────────────────────────────────────────────────────────────────

  dynamic "origin" {
    for_each = var.origins
    content {
      domain_name         = origin.value.domain_name
      origin_id           = origin.key
      origin_path         = origin.value.origin_path
      connection_attempts = origin.value.connection_attempts
      connection_timeout  = origin.value.connection_timeout

      dynamic "custom_origin_config" {
        for_each = origin.value.custom_origin_config != null ? [origin.value.custom_origin_config] : []
        content {
          http_port                = custom_origin_config.value.http_port
          https_port               = custom_origin_config.value.https_port
          origin_protocol_policy   = custom_origin_config.value.origin_protocol_policy
          origin_ssl_protocols     = custom_origin_config.value.origin_ssl_protocols
          origin_read_timeout      = custom_origin_config.value.origin_read_timeout
          origin_keepalive_timeout = custom_origin_config.value.origin_keepalive_timeout
        }
      }

      origin_access_control_id = origin.value.origin_access_control_id

      dynamic "custom_header" {
        for_each = origin.value.custom_headers
        content {
          name  = custom_header.key
          value = custom_header.value
        }
      }
    }
  }

  # ── Default Cache Behavior ─────────────────────────────────────────────────

  default_cache_behavior {
    target_origin_id       = var.default_cache_behavior.target_origin_id
    viewer_protocol_policy = var.default_cache_behavior.viewer_protocol_policy
    allowed_methods        = var.default_cache_behavior.allowed_methods
    cached_methods         = var.default_cache_behavior.cached_methods
    compress               = var.default_cache_behavior.compress

    cache_policy_id            = var.default_cache_behavior.cache_policy_id
    origin_request_policy_id   = var.default_cache_behavior.origin_request_policy_id
    response_headers_policy_id = var.default_cache_behavior.response_headers_policy_id

    min_ttl     = var.default_cache_behavior.cache_policy_id == null ? var.default_cache_behavior.min_ttl : null
    default_ttl = var.default_cache_behavior.cache_policy_id == null ? var.default_cache_behavior.default_ttl : null
    max_ttl     = var.default_cache_behavior.cache_policy_id == null ? var.default_cache_behavior.max_ttl : null

    dynamic "function_association" {
      for_each = var.default_cache_behavior.function_associations
      content {
        event_type   = function_association.value.event_type
        function_arn = function_association.value.function_arn
      }
    }

    dynamic "lambda_function_association" {
      for_each = var.default_cache_behavior.lambda_associations
      content {
        event_type   = lambda_function_association.value.event_type
        lambda_arn   = lambda_function_association.value.lambda_arn
        include_body = lambda_function_association.value.include_body
      }
    }

    field_level_encryption_id = var.default_cache_behavior.field_level_encryption_id
    trusted_key_groups        = var.default_cache_behavior.trusted_key_groups
    trusted_signers           = var.default_cache_behavior.trusted_signers
  }

  # ── Ordered Cache Behaviors ────────────────────────────────────────────────

  dynamic "ordered_cache_behavior" {
    for_each = var.ordered_cache_behaviors
    content {
      path_pattern           = ordered_cache_behavior.value.path_pattern
      target_origin_id       = ordered_cache_behavior.value.target_origin_id
      viewer_protocol_policy = ordered_cache_behavior.value.viewer_protocol_policy
      allowed_methods        = ordered_cache_behavior.value.allowed_methods
      cached_methods         = ordered_cache_behavior.value.cached_methods
      compress               = ordered_cache_behavior.value.compress

      cache_policy_id            = ordered_cache_behavior.value.cache_policy_id
      origin_request_policy_id   = ordered_cache_behavior.value.origin_request_policy_id
      response_headers_policy_id = ordered_cache_behavior.value.response_headers_policy_id

      min_ttl     = ordered_cache_behavior.value.cache_policy_id == null ? ordered_cache_behavior.value.min_ttl : null
      default_ttl = ordered_cache_behavior.value.cache_policy_id == null ? ordered_cache_behavior.value.default_ttl : null
      max_ttl     = ordered_cache_behavior.value.cache_policy_id == null ? ordered_cache_behavior.value.max_ttl : null

      dynamic "function_association" {
        for_each = ordered_cache_behavior.value.function_associations
        content {
          event_type   = function_association.value.event_type
          function_arn = function_association.value.function_arn
        }
      }

      dynamic "lambda_function_association" {
        for_each = ordered_cache_behavior.value.lambda_associations
        content {
          event_type   = lambda_function_association.value.event_type
          lambda_arn   = lambda_function_association.value.lambda_arn
          include_body = lambda_function_association.value.include_body
        }
      }

      field_level_encryption_id = ordered_cache_behavior.value.field_level_encryption_id
      trusted_key_groups        = ordered_cache_behavior.value.trusted_key_groups
      trusted_signers           = ordered_cache_behavior.value.trusted_signers
    }
  }

  # ── Viewer Certificate ─────────────────────────────────────────────────────

  viewer_certificate {
    acm_certificate_arn            = var.viewer_certificate != null ? var.viewer_certificate.acm_certificate_arn : null
    ssl_support_method             = var.viewer_certificate != null ? var.viewer_certificate.ssl_support_method : null
    minimum_protocol_version       = var.viewer_certificate != null ? var.viewer_certificate.minimum_protocol_version : "TLSv1.2_2021"
    iam_certificate_id             = var.viewer_certificate != null ? var.viewer_certificate.iam_certificate_id : null
    cloudfront_default_certificate = var.viewer_certificate == null ? true : null
  }

  # ── Standard Access Logging (optional) ──────────────────────────────────────

  dynamic "logging_config" {
    for_each = var.log_bucket != null ? [1] : []
    content {
      bucket          = var.log_bucket
      prefix          = var.log_prefix
      include_cookies = var.log_include_cookies
    }
  }

  # ── Restrictions ───────────────────────────────────────────────────────────

  restrictions {
    geo_restriction {
      restriction_type = var.geo_restriction_type
      locations        = var.geo_restriction_type != "none" ? var.geo_restriction_locations : []
    }
  }

  tags = var.tags
}

# ── Real-Time Log Configuration ──────────────────────────────────────────────

data "aws_iam_policy_document" "realtime_log_assume_role" {
  count = var.real_time_log_config != null ? 1 : 0

  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["cloudfront.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "realtime_log" {
  count = var.real_time_log_config != null ? 1 : 0

  name               = "cloudfront-realtime-log-${replace(var.name, "/[^a-zA-Z0-9]/", "-")}"
  assume_role_policy = data.aws_iam_policy_document.realtime_log_assume_role[0].json

  tags = var.tags
}

data "aws_iam_policy_document" "realtime_log" {
  count = var.real_time_log_config != null ? 1 : 0

  statement {
    actions = [
      "kinesis:DescribeStream",
      "kinesis:PutRecord",
      "kinesis:PutRecords",
    ]
    resources = [var.real_time_log_config.kinesis_stream_arn]
  }
}

resource "aws_iam_role_policy" "realtime_log" {
  count = var.real_time_log_config != null ? 1 : 0

  name   = "kinesis-put"
  role   = aws_iam_role.realtime_log[0].name
  policy = data.aws_iam_policy_document.realtime_log[0].json
}

resource "aws_cloudfront_realtime_log_config" "main" {
  count = var.real_time_log_config != null ? 1 : 0

  name          = var.real_time_log_config.name
  sampling_rate = var.real_time_log_config.sampling_rate
  fields        = var.real_time_log_config.fields

  endpoint {
    stream_type = "Kinesis"

    kinesis_stream_config {
      role_arn   = aws_iam_role.realtime_log[0].arn
      stream_arn = var.real_time_log_config.kinesis_stream_arn
    }
  }
}
