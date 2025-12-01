variable "api_name" {
  description = "Name of the HTTP API. Used in resource names and tags."
  type        = string
}

variable "description" {
  description = "Optional description for the HTTP API."
  type        = string
  default     = null
}

variable "stage_name" {
  description = "Stage name. Use '$default' for the auto-deploy stage."
  type        = string
  default     = "$default"
}

variable "auto_deploy" {
  description = "Whether to automatically deploy API changes to the stage."
  type        = bool
  default     = true
}

variable "access_log_enabled" {
  description = "Enable CloudWatch access logging for the stage."
  type        = bool
  default     = true
}

variable "access_log_retention_days" {
  description = "Retention period in days for access logs. 7 is the cost-aware default; override for prod."
  type        = number
  default     = 7

  validation {
    condition     = contains([1, 3, 5, 7, 14, 30, 60, 90, 120, 150, 180, 365, 400, 545, 731, 1096, 1827, 2192, 2557, 2922, 3288, 3653], var.access_log_retention_days)
    error_message = "access_log_retention_days must be a valid CloudWatch retention value."
  }
}

variable "cors_configuration" {
  description = "CORS configuration for the HTTP API. Set to null to disable CORS."
  type = object({
    allow_origins     = list(string)
    allow_methods     = list(string)
    allow_headers     = optional(list(string), ["Content-Type", "Authorization", "X-Api-Key"])
    expose_headers    = optional(list(string), [])
    max_age           = optional(number, 3600)
    allow_credentials = optional(bool, false)
  })
  default = null
}

variable "routes" {
  description = "Route definitions. Each route maps a route key (e.g. 'GET /items', '$default') to a Lambda function."
  type = map(object({
    route_key          = string
    lambda_arn         = string
    authorization_type = optional(string, "NONE")
  }))
  default = {}

  validation {
    condition = alltrue([
      for k, v in var.routes : contains(["NONE", "JWT"], v.authorization_type)
    ])
    error_message = "authorization_type must be 'NONE' or 'JWT'."
  }
}

variable "jwt_authorizer" {
  description = "Optional Cognito JWT authorizer. Routes with authorization_type='JWT' will use this."
  type = object({
    name     = string
    issuer   = string
    audience = list(string)
  })
  default = null
}

variable "custom_domain" {
  description = "Optional custom domain configuration."
  type = object({
    domain_name     = string
    certificate_arn = string
    route53_zone_id = optional(string)
  })
  default = null
}

variable "tags" {
  description = "Tags applied to all resources created by this module."
  type        = map(string)
  default     = {}
}
