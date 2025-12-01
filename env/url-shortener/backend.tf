# Remote state backend — S3 with DynamoDB locking.
#
# Before this backend works, run the bootstrap configuration ONCE per AWS account:
#   cd env/url-shortener/bootstrap
#   terraform init
#   terraform apply -var="bucket_name=<globally-unique-name>"
#
# Then replace CHANGE_ME with the bucket name from bootstrap output:
#   cd env/url-shortener/bootstrap && terraform output tfstate_bucket_name
#
# State is stored per-environment via the key prefix below.

terraform {
  backend "s3" {
    bucket         = "CHANGE_ME"
    key            = "url-shortener/prod/terraform.tfstate"
    region         = "us-east-1"
    dynamodb_table = "terraform-state-lock"
  }
}
