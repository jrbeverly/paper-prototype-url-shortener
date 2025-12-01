# CloudFront Distribution Module

## Purpose

Deploys a CloudFront distribution configured for the redirect platform's needs: multi-origin support, CloudFront Functions association, real-time logging to Kinesis, WAF integration, and custom SSL certificates.

## Usage

```hcl
module "cloudfront" {
  source = "../../../modules/cloudfront-distribution"

  name           = "url-shortener-${var.environment}"
  default_root_object = "index.html"

  aliases = ["cdn.example.com"]

  origins = {
    "s3-assets" = {
      domain_name              = module.assets_bucket.bucket_regional_domain_name
      origin_access_control_id = aws_cloudfront_origin_access_control.s3.id
    }
    "api" = {
      domain_name = trimsuffix(module.api_gateway.stage_invoke_url, "/")
      custom_origin_config = {
        origin_protocol_policy = "https-only"
      }
      custom_headers = {
        "X-Origin-Verify" = var.origin_secret
      }
    }
    "redirect" = {
      domain_name = trimsuffix(trimprefix(module.redirect_lambda.function_url, "https://"), "/")
      custom_origin_config = {
        origin_protocol_policy = "https-only"
        origin_read_timeout    = 5
      }
    }
  }

  default_cache_behavior = {
    target_origin_id = "redirect"
    allowed_methods  = ["GET", "HEAD", "OPTIONS"]
    cached_methods   = ["GET", "HEAD"]
    cache_policy_id  = data.aws_cloudfront_cache_policy.managed_caching_optimized.id

    function_associations = {
      viewer-request = {
        function_arn = aws_cloudfront_function.request_normalization.arn
        event_type   = "viewer-request"
      }
      viewer-response = {
        function_arn = aws_cloudfront_function.security_headers.arn
        event_type   = "viewer-response"
      }
    }
  }

  ordered_cache_behaviors = [
    {
      path_pattern     = "/api/*"
      target_origin_id = "api"
      cache_policy_id  = data.aws_cloudfront_cache_policy.managed_caching_disabled.id
    }
  ]

  viewer_certificate = {
    acm_certificate_arn = aws_acm_certificate.cdn.arn
    ssl_support_method  = "sni-only"
  }

  real_time_log_config = {
    name               = "redirect-clicks-${var.environment}"
    sampling_rate      = 100
    kinesis_stream_arn = module.click_stream.arn
  }

  web_acl_id = aws_wafv2_web_acl.distribution.arn

  tags = local.common_tags
}
```

## Inputs

| Name | Description | Type | Default | Required |
|---|---|---|---|---|
| `name` | Display name for the distribution | `string` | — | yes |
| `enabled` | Accept end-user requests | `bool` | `true` | no |
| `default_root_object` | Root URL object (e.g. `index.html`) | `string` | `null` | no |
| `price_class` | Edge location price class | `string` | `"PriceClass_100"` | no |
| `aliases` | Alternate domain names (CNAMEs) | `list(string)` | `[]` | no |
| `origins` | Origin definitions | `map(object)` | `{}` | no |
| `default_cache_behavior` | Default cache behavior | `object` | — | yes |
| `ordered_cache_behaviors` | Path-pattern cache behaviors | `list(object)` | `[]` | no |
| `viewer_certificate` | Custom SSL certificate | `object` | `null` | no |
| `web_acl_id` | WAF web ACL ARN | `string` | `null` | no |
| `real_time_log_config` | Real-time log to Kinesis | `object` | `null` | no |
| `log_bucket` | S3 bucket for standard access logs | `string` | `null` | no |
| `log_prefix` | Prefix for log objects | `string` | `null` | no |
| `log_include_cookies` | Include cookies in logs | `bool` | `false` | no |
| `geo_restriction_type` | Geo restriction mode | `string` | `"none"` | no |
| `geo_restriction_locations` | Country codes for geo restriction | `list(string)` | `[]` | no |
| `tags` | Tags applied to all resources | `map(string)` | `{}` | no |

### Origins Object

```hcl
origins = {
  "origin-id" = {
    domain_name         = "my-bucket.s3.us-east-1.amazonaws.com"
    origin_path         = "/prefix"               # optional
    connection_attempts = 3                        # optional (default: 3)
    connection_timeout  = 10                       # optional (default: 10)

    # Exactly one of:
    custom_origin_config = {                       # For HTTP/HTTPS origins
      origin_protocol_policy = "https-only"        # required
      http_port              = 80                  # optional
      https_port             = 443                 # optional
      origin_ssl_protocols   = ["TLSv1.2"]         # optional
      origin_read_timeout    = 30                  # optional
      origin_keepalive_timeout = 5                 # optional
    }
    origin_access_control_id = "..."               # For S3 origins (OAC)

    custom_headers = {                             # optional
      "X-Custom" = "value"
    }
  }
}
```

### Cache Behavior Object

```hcl
default_cache_behavior = {
  target_origin_id       = "api"
  viewer_protocol_policy = "redirect-to-https"     # optional (default)
  allowed_methods        = ["GET", "HEAD", "OPTIONS"]
  cached_methods         = ["GET", "HEAD"]
  compress               = true                    # optional (default: true)

  # Managed policy (preferred): one of cache_policy_id, or TTLs below
  cache_policy_id            = "658327ea-f89d-4fab-a63d-7e88639e58f6"
  origin_request_policy_id   = "88a5eaf4-2fd4-4709-b370-b4c650ea3fcf"
  response_headers_policy_id = null
  # Legacy TTLs (ignored when cache_policy_id is set):
  min_ttl     = 0
  default_ttl = 3600
  max_ttl     = 86400

  function_associations = {
    "viewer-request" = {
      function_arn = "arn:aws:cloudfront::..."
      event_type   = "viewer-request"
    }
  }

  lambda_associations = {
    "origin-request" = {
      lambda_arn   = "arn:aws:lambda::..."
      event_type   = "origin-request"
      include_body = false
    }
  }
}
```

## Outputs

| Name | Description |
|---|---|
| `distribution_id` | CloudFront distribution ID |
| `distribution_arn` | CloudFront distribution ARN |
| `distribution_domain_name` | CloudFront-assigned domain name |
| `distribution_hosted_zone_id` | Hosted zone ID for Route53 ALIAS records |
| `realtime_log_config_arn` | Real-time log config ARN (if enabled) |
| `realtime_log_role_arn` | IAM role ARN for Kinesis delivery (if enabled) |

## Cost Expectations

- **CloudFront distribution**: No base cost — pay per request + data transfer
- **PriceClass_100** (US/Europe only): lowest data transfer pricing
- **Real-time logs**: $0.01 per 1,000,000 log records delivered to Kinesis
- **Free tier**: 1 TB data transfer/month, 10M HTTP requests/month (first 12 months)

For low-traffic scenarios (< 1M requests/month), the distribution portion costs < $1.00/month plus data transfer.
