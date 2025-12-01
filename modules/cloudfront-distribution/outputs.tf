output "distribution_id" {
  description = "CloudFront distribution ID (e.g. EXXXXXXXXXXXXX)."
  value       = aws_cloudfront_distribution.main.id
}

output "distribution_arn" {
  description = "CloudFront distribution ARN."
  value       = aws_cloudfront_distribution.main.arn
}

output "distribution_domain_name" {
  description = "CloudFront-assigned domain name (e.g. d1234567890.cloudfront.net)."
  value       = aws_cloudfront_distribution.main.domain_name
}

output "distribution_hosted_zone_id" {
  description = "CloudFront hosted zone ID. Use for Route53 ALIAS records pointing at this distribution."
  value       = aws_cloudfront_distribution.main.hosted_zone_id
}

output "realtime_log_config_arn" {
  description = "ARN of the real-time log configuration, if enabled."
  value       = try(aws_cloudfront_realtime_log_config.main[0].arn, null)
}

output "realtime_log_role_arn" {
  description = "ARN of the IAM role for real-time log delivery to Kinesis, if enabled."
  value       = try(aws_iam_role.realtime_log[0].arn, null)
}
