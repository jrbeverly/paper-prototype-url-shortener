variable "name" {
  description = "Display name for the CloudFront distribution. Used as the human-readable identifier."
  type        = string
}

variable "enabled" {
  description = "Whether the distribution is enabled to accept end-user requests."
  type        = bool
  default     = true
}

variable "default_root_object" {
  description = "Object to return for requests to the root URL (e.g. index.html)."
  type        = string
  default     = null
}

variable "price_class" {
  description = "Price class for edge location distribution. PriceClass_100 (US/Europe only) is the cheapest."
  type        = string
  default     = "PriceClass_100"

  validation {
    condition     = contains(["PriceClass_All", "PriceClass_200", "PriceClass_100"], var.price_class)
    error_message = "price_class must be PriceClass_All, PriceClass_200, or PriceClass_100."
  }
}

variable "aliases" {
  description = "Alternate domain names (CNAMEs) for the distribution. Requires a valid ACM certificate matching these names."
  type        = list(string)
  default     = []
}

variable "origins" {
  description = "Map of origin definitions keyed by a logical origin ID."
  type = map(object({
    domain_name         = string
    origin_path         = optional(string)
    connection_attempts = optional(number, 3)
    connection_timeout  = optional(number, 10)
    custom_origin_config = optional(object({
      http_port                = optional(number, 80)
      https_port               = optional(number, 443)
      origin_protocol_policy   = string
      origin_ssl_protocols     = optional(list(string), ["TLSv1.2"])
      origin_read_timeout      = optional(number, 30)
      origin_keepalive_timeout = optional(number, 5)
    }))
    origin_access_control_id = optional(string)
    custom_headers           = optional(map(string), {})
  }))
  default = {}
}

variable "default_cache_behavior" {
  description = "Default cache behavior applied to all requests that don't match an ordered behavior."
  type = object({
    target_origin_id       = string
    viewer_protocol_policy = optional(string, "redirect-to-https")
    allowed_methods        = optional(list(string), ["GET", "HEAD", "OPTIONS"])
    cached_methods         = optional(list(string), ["GET", "HEAD"])
    compress               = optional(bool, true)

    cache_policy_id            = optional(string)
    origin_request_policy_id   = optional(string)
    response_headers_policy_id = optional(string)

    min_ttl     = optional(number, 0)
    default_ttl = optional(number, 3600)
    max_ttl     = optional(number, 86400)

    function_associations = optional(map(object({
      function_arn = string
      event_type   = string
    })), {})

    lambda_associations = optional(map(object({
      lambda_arn   = string
      event_type   = string
      include_body = optional(bool, false)
    })), {})

    field_level_encryption_id = optional(string)
    trusted_key_groups        = optional(list(string))
    trusted_signers           = optional(list(string))
  })

  validation {
    condition = alltrue([
      for k, v in var.default_cache_behavior.function_associations :
      contains(["viewer-request", "viewer-response", "origin-request", "origin-response"], v.event_type)
    ])
    error_message = "function_associations event_type must be viewer-request, viewer-response, origin-request, or origin-response."
  }

  validation {
    condition = alltrue([
      for k, v in var.default_cache_behavior.lambda_associations :
      contains(["viewer-request", "viewer-response", "origin-request", "origin-response"], v.event_type)
    ])
    error_message = "lambda_associations event_type must be viewer-request, viewer-response, origin-request, or origin-response."
  }
}

variable "ordered_cache_behaviors" {
  description = "Ordered cache behaviors evaluated before the default behavior. Order is significant — first match wins."
  type = list(object({
    path_pattern           = string
    target_origin_id       = string
    viewer_protocol_policy = optional(string, "redirect-to-https")
    allowed_methods        = optional(list(string), ["GET", "HEAD", "OPTIONS"])
    cached_methods         = optional(list(string), ["GET", "HEAD"])
    compress               = optional(bool, true)

    cache_policy_id            = optional(string)
    origin_request_policy_id   = optional(string)
    response_headers_policy_id = optional(string)

    min_ttl     = optional(number, 0)
    default_ttl = optional(number, 3600)
    max_ttl     = optional(number, 86400)

    function_associations = optional(map(object({
      function_arn = string
      event_type   = string
    })), {})

    lambda_associations = optional(map(object({
      lambda_arn   = string
      event_type   = string
      include_body = optional(bool, false)
    })), {})

    field_level_encryption_id = optional(string)
    trusted_key_groups        = optional(list(string))
    trusted_signers           = optional(list(string))
  }))
  default = []
}

variable "viewer_certificate" {
  description = "SSL/TLS certificate configuration. Omit to use the default CloudFront certificate (*.cloudfront.net)."
  type = object({
    acm_certificate_arn      = optional(string)
    ssl_support_method       = optional(string, "sni-only")
    minimum_protocol_version = optional(string, "TLSv1.2_2021")
    iam_certificate_id       = optional(string)
  })
  default = null
}

variable "web_acl_id" {
  description = "WAF web ACL ARN to associate with the distribution. Omit for no WAF protection."
  type        = string
  default     = null
}

variable "real_time_log_config" {
  description = "Real-time log configuration. Logs are delivered to Kinesis Data Streams within seconds."
  type = object({
    name          = string
    sampling_rate = optional(number, 100)
    fields = optional(list(string), [
      "timestamp",
      "c-ip",
      "time-to-first-byte",
      "sc-status",
      "sc-bytes",
      "cs-method",
      "cs-protocol",
      "cs-host",
      "cs-uri-stem",
      "cs-uri-query",
      "cs-user-agent",
      "cs-referer",
      "cs-cookie",
      "cs-header-host",
      "x-edge-location",
      "x-edge-request-id",
      "x-host-header",
      "time-taken",
      "cs-protocol-version",
      "c-ip-version",
      "sc-range-start",
      "sc-range-end",
      "ssl-protocol",
      "ssl-cipher",
      "x-edge-result-type",
      "cs-accept-encoding",
      "cs-header-names",
      "cs-header-length",
    ])
    kinesis_stream_arn = string
  })
  default = null
}

variable "log_bucket" {
  description = "S3 bucket domain name for standard access logs. Omit to disable standard logging."
  type        = string
  default     = null
}

variable "log_prefix" {
  description = "Optional prefix for standard access log objects."
  type        = string
  default     = null
}

variable "log_include_cookies" {
  description = "Include cookies in standard access logs."
  type        = bool
  default     = false
}

variable "geo_restriction_type" {
  description = "Geo restriction type. 'none' for no restrictions, 'whitelist' to allow only listed countries, 'blacklist' to block listed countries."
  type        = string
  default     = "none"

  validation {
    condition     = contains(["none", "whitelist", "blacklist"], var.geo_restriction_type)
    error_message = "geo_restriction_type must be none, whitelist, or blacklist."
  }
}

variable "geo_restriction_locations" {
  description = "List of two-letter country codes for geo restriction. Only used when geo_restriction_type is not 'none'."
  type        = list(string)
  default     = []
}

variable "tags" {
  description = "Tags applied to all resources created by this module."
  type        = map(string)
  default     = {}
}
