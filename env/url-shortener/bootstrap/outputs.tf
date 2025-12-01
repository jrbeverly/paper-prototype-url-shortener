output "tfstate_bucket_name" {
  description = "S3 bucket name for Terraform state. Use this value in the main backend.tf."
  value       = aws_s3_bucket.tfstate.id
}

output "dynamodb_table_name" {
  description = "DynamoDB table name for state locking."
  value       = aws_dynamodb_table.tfstate_lock.id
}
