# API Gateway HTTP API Module

## Purpose

Deploys an API Gateway HTTP API (v2) with Lambda proxy integrations. HTTP API is the serverless, lower-cost variant — ~70% cheaper per request than REST API and lower latency (no CloudFront front-end required).

## Usage

```hcl
module "api_gateway" {
  source = "../../../modules/api-gateway"

  api_name    = "control-plane-${var.environment}"
  description = "ControlPlane management API"

  routes = {
    "$default" = {
      route_key  = "$default"
      lambda_arn = module.control_plane_lambda.arn
    }
  }

  cors_configuration = {
    allow_origins = ["https://app.example.com"]
    allow_methods = ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"]
  }

  custom_domain = {
    domain_name     = "api.example.com"
    certificate_arn = "arn:aws:acm:us-east-1:123456789012:certificate/..."
    route53_zone_id = "Z1234567890"
  }

  jwt_authorizer = {
    name     = "cognito"
    issuer   = "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_xxxxx"
    audience = ["xxxxxxxxxxxx"]
  }

  tags = local.common_tags
}
```

## Inputs

| Name | Description | Type | Default | Required |
|---|---|---|---|---|
| `api_name` | API name for naming and discovery | `string` | — | yes |
| `description` | API description | `string` | `null` | no |
| `stage_name` | Stage name (`$default` for auto-deploy) | `string` | `"$default"` | no |
| `auto_deploy` | Auto-deploy changes to the stage | `bool` | `true` | no |
| `access_log_enabled` | Enable CloudWatch access logging | `bool` | `true` | no |
| `access_log_retention_days` | Access log retention in days | `number` | `7` | no |
| `cors_configuration` | CORS settings (null disables) | `object` | `null` | no |
| `routes` | Route definitions | `map(object)` | `{}` | no |
| `jwt_authorizer` | Cognito JWT authorizer config | `object` | `null` | no |
| `custom_domain` | Custom domain config | `object` | `null` | no |
| `tags` | Tags applied to all resources | `map(string)` | `{}` | no |

### Routes Object

```hcl
routes = {
  "route-name" = {
    route_key            = "GET /items"  # Route key (method + path, or $default)
    lambda_arn           = "arn:..."     # Lambda function ARN
    authorization_type   = "NONE"        # "NONE" or "JWT" (default: "NONE")
  }
}
```

### CORS Configuration Object

```hcl
cors_configuration = {
  allow_origins     = ["https://app.example.com"]
  allow_methods     = ["GET", "POST", "PUT", "DELETE", "OPTIONS"]
  allow_headers     = ["Content-Type", "Authorization", "X-Api-Key"]
  expose_headers    = ["X-Correlation-ID"]
  max_age           = 3600
  allow_credentials = false
}
```

## Outputs

| Name | Description |
|---|---|
| `api_endpoint` | Invoke URL |
| `api_id` | API ID |
| `api_arn` | API ARN |
| `api_execution_arn` | Execution ARN (for IAM policies) |
| `stage_name` | Stage name |
| `stage_arn` | Stage ARN |
| `stage_invoke_url` | Full invoke URL with stage |
| `custom_domain_name` | Custom domain hostname (if configured) |
| `custom_domain_regional_name` | CloudFront-assigned domain for DNS target |

## Cost Expectations

- **API Gateway**: $1.00 per million requests (HTTP API rate; REST API is $3.50/M)
- **CloudWatch Logs**: ~$0.50/GB ingested; 7-day retention keeps storage costs near zero
- **Lambda**: Pay-per-invocation (see Lambda module)

For low-traffic scenarios (< 1M requests/month), the HTTP API portion costs < $1.00/month.
