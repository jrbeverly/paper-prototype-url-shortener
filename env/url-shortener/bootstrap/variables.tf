variable "aws_region" {
  description = "AWS region for the state bucket and lock table."
  type        = string
  default     = "us-east-1"
}

variable "bucket_name" {
  description = "Globally unique S3 bucket name for Terraform state. Recommended pattern: short-io-tfstate-<account-id>"
  type        = string
}

variable "dynamodb_table_name" {
  description = "DynamoDB table name for state locking."
  type        = string
  default     = "terraform-state-lock"
}
