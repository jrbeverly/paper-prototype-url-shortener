variable "environment" {
  description = "Deployment environment (prod, staging, sandbox)."
  type        = string
}

variable "service_name" {
  description = "Service slug used to namespace rule names (e.g. url-shortener)."
  type        = string
}

variable "success_sample_rate" {
  description = "Fraction of normal requests to sample (0.0–1.0). 0.10 = 10%."
  type        = number
  default     = 0.10

  validation {
    condition     = var.success_sample_rate >= 0 && var.success_sample_rate <= 1
    error_message = "success_sample_rate must be between 0.0 and 1.0."
  }
}

variable "reservoir_size" {
  description = "Number of requests per second sampled before the fixed rate applies."
  type        = number
  default     = 5
}

variable "tags" {
  description = "Tags applied to every X-Ray resource created by this module."
  type        = map(string)
  default     = {}
}
