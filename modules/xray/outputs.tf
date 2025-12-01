output "control_plane_group_arn" {
  description = "ARN of the X-Ray group for the ControlPlane service."
  value       = aws_xray_group.control_plane.arn
}

output "redirect_service_group_arn" {
  description = "ARN of the X-Ray group for the RedirectService hot path."
  value       = aws_xray_group.redirect_service.arn
}

output "default_sampling_rule_arn" {
  description = "ARN of the default sampling rule."
  value       = aws_xray_sampling_rule.default.arn
}
