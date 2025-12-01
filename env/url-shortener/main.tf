provider "aws" {
  region = var.aws_region

  default_tags {
    tags = local.common_tags
  }
}

# DR region provider — us-west-2 for cross-region S3 replication and
# cold-failover recovery procedures (ADR-013).
provider "aws" {
  alias  = "dr"
  region = var.dr_region

  default_tags {
    tags = merge(local.common_tags, { "DR" = "true" })
  }
}

# Authentication uses the standard AWS SDK credential chain —
# environment variables, IAM roles, or SSO. No hardcoded credentials.
locals {
  common_tags = {
    Environment = var.environment
    Service     = "url-shortener"
    ManagedBy   = "terraform"
    Owner       = "platform"
  }
}

variable "aws_region" {
  description = "AWS region to deploy into."
  type        = string
  default     = "us-east-1"
}

variable "environment" {
  description = "Deployment environment (prod, staging, sandbox)."
  type        = string
  default     = "prod"
}

variable "dr_region" {
  description = "AWS region for disaster recovery (cross-region replication, cold failover)."
  type        = string
  default     = "us-west-2"
}
