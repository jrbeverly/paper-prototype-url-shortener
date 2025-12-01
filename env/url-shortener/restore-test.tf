# Automated restore test — Lambda function that validates DynamoDB PITR
# restore functionality on a quarterly schedule per ADR-013.
#
# The function:
#   1. Records the current UTC time as the restore point
#   2. Restores the redirects table to a new test table from PITR
#   3. Validates the restored table schema and item count
#   4. Deletes the test table
#   5. Reports success/failure via CloudWatch Logs
#
# Triggered by EventBridge Scheduler on a quarterly cron expression.
# Failures post to the backup-alerts SNS topic (sns-notifications.tf).

# ── Lambda function ───────────────────────────────────────────────────────

resource "aws_lambda_function" "restore_test" {
  function_name = "restore-test-${var.environment}"
  description   = "Quarterly automated DynamoDB PITR restore validation"

  # Lambda code package is built and uploaded by CI pipeline.
  # For local deployment, use: dotnet lambda deploy-function
  filename         = data.archive_file.restore_test.output_path
  source_code_hash = data.archive_file.restore_test.output_base64sha256

  role          = aws_iam_role.restore_test_lambda.arn
  handler       = "BackupRestoreTest.Function::BackupRestoreTest.Function.Function::HandleAsync"
  runtime       = "dotnet8"
  architectures = ["arm64"]
  memory_size   = 256
  timeout       = 600 # Restore test can take up to 5-10 minutes for large tables

  environment {
    variables = {
      REDIRECTS_TABLE_NAME = aws_dynamodb_table.redirects.name
      SNS_TOPIC_ARN        = aws_sns_topic.backup_alerts.arn
    }
  }

  # X-Ray active tracing for observability into restore operations
  tracing_config {
    mode = "Active"
  }

  tags = local.common_tags
}

# ── Placeholder deployment package ─────────────────────────────────────────
# The actual deployment package is built by the CI pipeline and uploaded
# as part of the deployment process. This data archive is a placeholder
# to satisfy the Terraform resource dependency graph.
#
# In practice, CI runs:
#   dotnet lambda deploy-function restore-test-prod
# which handles building, packaging, and uploading.

data "archive_file" "restore_test" {
  type        = "zip"
  output_path = "${path.module}/.terraform/restore-test-placeholder.zip"

  source {
    content  = "{}"
    filename = "placeholder.json"
  }
}

# ── CloudWatch log group ──────────────────────────────────────────────────

resource "aws_cloudwatch_log_group" "restore_test" {
  name              = "/aws/lambda/restore-test-${var.environment}"
  retention_in_days = 14 # Restore test runs quarterly; 14 days covers retries

  tags = local.common_tags
}

# ── Lambda IAM role ───────────────────────────────────────────────────────

data "aws_iam_policy_document" "restore_test_assume_role" {
  statement {
    effect = "Allow"
    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
    actions = ["sts:AssumeRole"]
  }
}

resource "aws_iam_role" "restore_test_lambda" {
  name               = "restore-test-lambda-${var.environment}"
  assume_role_policy = data.aws_iam_policy_document.restore_test_assume_role.json

  tags = local.common_tags
}

data "aws_iam_policy_document" "restore_test_lambda" {
  # Allow PITR restore from the redirects table
  statement {
    effect = "Allow"
    actions = [
      "dynamodb:RestoreTableToPointInTime",
      "dynamodb:DescribeTable",
      "dynamodb:Scan",
      "dynamodb:DeleteTable",
      "dynamodb:ListTables",
    ]
    resources = [
      aws_dynamodb_table.redirects.arn,
      "${aws_dynamodb_table.redirects.arn}/*",
      "arn:aws:dynamodb:${var.aws_region}:${data.aws_caller_identity.current.account_id}:table/redirects-${var.environment}-dr-test-*",
    ]
  }

  # CloudWatch Logs: write restore test results
  statement {
    effect = "Allow"
    actions = [
      "logs:CreateLogGroup",
      "logs:CreateLogStream",
      "logs:PutLogEvents",
    ]
    resources = ["${aws_cloudwatch_log_group.restore_test.arn}:*"]
  }

  # Publish restore test results to SNS on failure
  statement {
    effect    = "Allow"
    actions   = ["sns:Publish"]
    resources = [aws_sns_topic.backup_alerts.arn]
  }
}

resource "aws_iam_role_policy" "restore_test_lambda" {
  name   = "restore-test-lambda-policy-${var.environment}"
  role   = aws_iam_role.restore_test_lambda.id
  policy = data.aws_iam_policy_document.restore_test_lambda.json
}

# ── EventBridge Scheduler — quarterly restore test ────────────────────────

# IAM role for EventBridge to invoke the Lambda
data "aws_iam_policy_document" "scheduler_assume_role" {
  statement {
    effect = "Allow"
    principals {
      type        = "Service"
      identifiers = ["scheduler.amazonaws.com"]
    }
    actions = ["sts:AssumeRole"]
  }
}

resource "aws_iam_role" "scheduler_restore_test" {
  name               = "scheduler-restore-test-${var.environment}"
  assume_role_policy = data.aws_iam_policy_document.scheduler_assume_role.json

  tags = local.common_tags
}

data "aws_iam_policy_document" "scheduler_restore_test" {
  statement {
    effect    = "Allow"
    actions   = ["lambda:InvokeFunction"]
    resources = [aws_lambda_function.restore_test.arn]
  }
}

resource "aws_iam_role_policy" "scheduler_restore_test" {
  name   = "scheduler-restore-test-policy-${var.environment}"
  role   = aws_iam_role.scheduler_restore_test.id
  policy = data.aws_iam_policy_document.scheduler_restore_test.json
}

# Schedule: quarterly at 01:00 UTC on the 15th of March, June, September, December
# This runs outside peak load and before the quarterly DR test review (see dr-testing-schedule.md).
resource "aws_scheduler_schedule" "restore_test" {
  name = "restore-test-quarterly-${var.environment}"

  flexible_time_window {
    mode                      = "FLEXIBLE"
    maximum_window_in_minutes = 60 # Allow 1-hour flexible start window
  }

  schedule_expression          = "cron(0 1 15 3,6,9,12 ? *)"
  schedule_expression_timezone = "UTC"

  target {
    arn      = aws_lambda_function.restore_test.arn
    role_arn = aws_iam_role.scheduler_restore_test.arn

    retry_policy {
      maximum_retry_attempts       = 2
      maximum_event_age_in_seconds = 3600 # 1 hour
    }
  }
}
