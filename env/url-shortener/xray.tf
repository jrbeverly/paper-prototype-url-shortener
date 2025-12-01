# X-Ray distributed tracing: sampling rules and service-map groups.
#
# Lambda active tracing must be enabled on each function resource:
#   tracing_config { mode = "Active" }
#
# API Gateway (REST API) tracing is enabled per stage:
#   xray_tracing_enabled = true
#
# CloudFront tracing is enabled on the distribution:
#   default_cache_behavior { ... x_ray_enabled = true }

module "xray" {
  source = "../../modules/xray"

  environment         = var.environment
  service_name        = "url-shortener"
  success_sample_rate = 0.10
  reservoir_size      = 5

  tags = local.common_tags
}
