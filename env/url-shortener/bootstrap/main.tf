# Bootstrap resources for Terraform remote state backend.
#
# This directory creates the S3 bucket and DynamoDB table that the main
# Terraform configuration uses for remote state storage and locking.
# It uses local state — the remote backend is what we're bootstrapping.
#
# Run this ONCE per AWS account before using the main Terraform configuration:
#   cd env/url-shortener/bootstrap
#   terraform init
#   terraform plan   -var="bucket_name=<globally-unique-name>"
#   terraform apply  -var="bucket_name=<globally-unique-name>"
#
# Then configure the main backend.tf with the output bucket name.

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = local.common_tags
  }
}

locals {
  common_tags = {
    Environment = "bootstrap"
    Service     = "terraform-state"
    ManagedBy   = "terraform"
    Owner       = "platform"
  }
}

# ── State bucket ──────────────────────────────────────────────────────────

resource "aws_s3_bucket" "tfstate" {
  bucket = var.bucket_name
}

resource "aws_s3_bucket_versioning" "tfstate" {
  bucket = aws_s3_bucket.tfstate.id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "tfstate" {
  bucket = aws_s3_bucket.tfstate.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_public_access_block" "tfstate" {
  bucket = aws_s3_bucket.tfstate.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# ── Lock table ────────────────────────────────────────────────────────────

resource "aws_dynamodb_table" "tfstate_lock" {
  name         = var.dynamodb_table_name
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "LockID"

  attribute {
    name = "LockID"
    type = "S"
  }

  tags = local.common_tags
}
