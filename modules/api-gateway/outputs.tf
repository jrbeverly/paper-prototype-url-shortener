output "api_endpoint" {
  description = "Invoke URL for the HTTP API (e.g. https://{api-id}.execute-api.{region}.amazonaws.com)."
  value       = aws_apigatewayv2_api.main.api_endpoint
}

output "api_id" {
  description = "ID of the HTTP API."
  value       = aws_apigatewayv2_api.main.id
}

output "api_arn" {
  description = "ARN of the HTTP API."
  value       = aws_apigatewayv2_api.main.arn
}

output "api_execution_arn" {
  description = "Execution ARN of the HTTP API. Used for constructing IAM policy source ARNs."
  value       = aws_apigatewayv2_api.main.execution_arn
}

output "stage_name" {
  description = "Name of the deployed stage."
  value       = aws_apigatewayv2_stage.main.name
}

output "stage_arn" {
  description = "ARN of the deployed stage."
  value       = aws_apigatewayv2_stage.main.arn
}

output "stage_invoke_url" {
  description = "Full invoke URL including the stage."
  value       = aws_apigatewayv2_stage.main.invoke_url
}

output "custom_domain_name" {
  description = "Custom domain hostname, if configured."
  value       = try(aws_apigatewayv2_domain_name.custom[0].domain_name, null)
}

output "custom_domain_regional_name" {
  description = "CloudFront-assigned regional domain name for the custom domain, if configured. Use as the CNAME/ALIAS target."
  value       = try(aws_apigatewayv2_domain_name.custom[0].domain_name_configuration[0].target_domain_name, null)
}
