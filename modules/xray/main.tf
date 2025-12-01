terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = ">= 5.0"
    }
  }
}

# Health-check probes are frequent and low-value; sample at 1% with a
# reservoir of 1 req/s so cost stays near zero.
resource "aws_xray_sampling_rule" "health_checks" {
  rule_name      = "${var.service_name}-${var.environment}-health"
  priority       = 100
  reservoir_size = 1
  fixed_rate     = 0.01
  url_path       = "/health"
  host           = "*"
  http_method    = "GET"
  service_name   = "*"
  service_type   = "*"
  resource_arn   = "*"
  version        = 1
  attributes     = {}

  tags = var.tags
}

# Default rule: configured sample rate for all other traffic.
# X-Ray head-based sampling decides at request start; errors that happen to
# be sampled are marked as faults by the SDK so they are filterable in the
# X-Ray console and service map.
resource "aws_xray_sampling_rule" "default" {
  rule_name      = "${var.service_name}-${var.environment}-default"
  priority       = 9000
  reservoir_size = var.reservoir_size
  fixed_rate     = var.success_sample_rate
  url_path       = "*"
  host           = "*"
  http_method    = "*"
  service_name   = "*"
  service_type   = "*"
  resource_arn   = "*"
  version        = 1
  attributes     = {}

  tags = var.tags
}

# X-Ray group for the ControlPlane — scopes the service map to management traffic.
resource "aws_xray_group" "control_plane" {
  group_name        = "${var.service_name}-${var.environment}-control-plane"
  filter_expression = "service(\"control-plane\")"

  tags = var.tags
}

# X-Ray group for the RedirectService — isolates the redirect hot path.
resource "aws_xray_group" "redirect_service" {
  group_name        = "${var.service_name}-${var.environment}-redirect"
  filter_expression = "service(\"redirect-service\")"

  tags = var.tags
}
