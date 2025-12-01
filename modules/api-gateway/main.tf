terraform {
  required_version = ">= 1.6, < 2.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = ">= 5.0"
    }
  }
}

data "aws_caller_identity" "current" {}

# ── HTTP API ────────────────────────────────────────────────────────────────

resource "aws_apigatewayv2_api" "main" {
  name          = var.api_name
  description   = var.description
  protocol_type = "HTTP"

  dynamic "cors_configuration" {
    for_each = var.cors_configuration != null ? [1] : []
    content {
      allow_origins     = var.cors_configuration.allow_origins
      allow_methods     = var.cors_configuration.allow_methods
      allow_headers     = var.cors_configuration.allow_headers
      expose_headers    = var.cors_configuration.expose_headers
      max_age           = var.cors_configuration.max_age
      allow_credentials = var.cors_configuration.allow_credentials
    }
  }

  tags = var.tags
}

# ── JWT Authorizer (optional, for Cognito) ──────────────────────────────────

resource "aws_apigatewayv2_authorizer" "jwt" {
  count = var.jwt_authorizer != null ? 1 : 0

  api_id           = aws_apigatewayv2_api.main.id
  authorizer_type  = "JWT"
  identity_sources = ["$request.header.Authorization"]
  name             = var.jwt_authorizer.name

  jwt_configuration {
    issuer   = var.jwt_authorizer.issuer
    audience = var.jwt_authorizer.audience
  }
}

# ── Access Logging ──────────────────────────────────────────────────────────

resource "aws_cloudwatch_log_group" "access" {
  count = var.access_log_enabled ? 1 : 0

  name              = "/aws/apigateway/${var.api_name}-access"
  retention_in_days = var.access_log_retention_days

  tags = var.tags
}

# ── Stage ───────────────────────────────────────────────────────────────────

resource "aws_apigatewayv2_stage" "main" {
  api_id      = aws_apigatewayv2_api.main.id
  name        = var.stage_name
  auto_deploy = var.auto_deploy

  dynamic "access_log_settings" {
    for_each = var.access_log_enabled ? [1] : []
    content {
      destination_arn = aws_cloudwatch_log_group.access[0].arn
      format = jsonencode({
        requestId               = "$context.requestId"
        ip                      = "$context.identity.sourceIp"
        requestTime             = "$context.requestTime"
        httpMethod              = "$context.httpMethod"
        routeKey                = "$context.routeKey"
        status                  = "$context.status"
        protocol                = "$context.protocol"
        responseLength          = "$context.responseLength"
        integrationErrorMessage = "$context.integrationErrorMessage"
        integrationStatus       = "$context.integrationStatus"
      })
    }
  }

  tags = var.tags
}

# ── Lambda Integrations ─────────────────────────────────────────────────────

resource "aws_apigatewayv2_integration" "route" {
  for_each = var.routes

  api_id                 = aws_apigatewayv2_api.main.id
  integration_type       = "AWS_PROXY"
  integration_uri        = each.value.lambda_arn
  payload_format_version = "2.0"
}

# ── Routes ──────────────────────────────────────────────────────────────────

resource "aws_apigatewayv2_route" "route" {
  for_each = var.routes

  api_id             = aws_apigatewayv2_api.main.id
  route_key          = each.value.route_key
  target             = "integrations/${aws_apigatewayv2_integration.route[each.key].id}"
  authorization_type = each.value.authorization_type
  authorizer_id      = each.value.authorization_type == "JWT" ? aws_apigatewayv2_authorizer.jwt[0].id : null
}

# ── Lambda Permissions ──────────────────────────────────────────────────────

resource "aws_lambda_permission" "route" {
  for_each = var.routes

  statement_id  = "AllowAPIGatewayInvoke-${replace(each.key, "/[^a-zA-Z0-9]/", "")}"
  action        = "lambda:InvokeFunction"
  function_name = each.value.lambda_arn
  principal     = "apigateway.amazonaws.com"
  source_arn    = "arn:aws:execute-api:${data.aws_caller_identity.current.arn}:${aws_apigatewayv2_api.main.id}/*/*"
}

# ── Custom Domain (optional) ────────────────────────────────────────────────

resource "aws_apigatewayv2_domain_name" "custom" {
  count = var.custom_domain != null ? 1 : 0

  domain_name = var.custom_domain.domain_name

  domain_name_configuration {
    certificate_arn = var.custom_domain.certificate_arn
    endpoint_type   = "REGIONAL"
    security_policy = "TLS_1_2"
  }

  tags = var.tags
}

resource "aws_apigatewayv2_api_mapping" "custom" {
  count = var.custom_domain != null ? 1 : 0

  api_id      = aws_apigatewayv2_api.main.id
  domain_name = aws_apigatewayv2_domain_name.custom[0].domain_name
  stage       = aws_apigatewayv2_stage.main.id
}

# ── Route53 DNS record for custom domain ────────────────────────────────────

resource "aws_route53_record" "custom" {
  count = var.custom_domain != null && var.custom_domain.route53_zone_id != null ? 1 : 0

  zone_id = var.custom_domain.route53_zone_id
  name    = var.custom_domain.domain_name
  type    = "A"

  alias {
    name                   = aws_apigatewayv2_domain_name.custom[0].domain_name_configuration[0].target_domain_name
    zone_id                = aws_apigatewayv2_domain_name.custom[0].domain_name_configuration[0].hosted_zone_id
    evaluate_target_health = false
  }
}
