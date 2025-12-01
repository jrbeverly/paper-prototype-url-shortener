# Redirect lookup table — DynamoDB single-table design per ADR-010.
# Point-in-Time Recovery (PITR) is enabled for disaster recovery per ADR-013.
#
# Key schema:
#   PK = HOST#{hostname}#SLUG#{slug}  (Partition Key)
#   SK = CONFIG                       (Sort Key)
#   GSI1PK = TENANT#{tenant_id}       (GSI1 Partition Key)
#   GSI1SK = DOMAIN#{domain_id}#SLUG#{slug} (GSI1 Sort Key)
#
# PITR provides continuous backup with 35-day retention.
# Restore creates a new table from any timestamp in the recovery window.
#
# On-demand billing: pay-per-request, zero-cost-when-idle.
# Capacity planning is deferred until traffic patterns stabilize (ADR-002).

resource "aws_dynamodb_table" "redirects" {
  name         = "redirects-${var.environment}"
  billing_mode = "PAY_PER_REQUEST"

  # PITR — continuous backup, restorable to any second in the last 35 days.
  # Enabled for all environments; the cost premium (~20% storage) is justified
  # by eliminating the risk of unrecoverable data loss (ADR-013).
  point_in_time_recovery {
    enabled = var.environment == "prod" || var.environment == "staging"
  }

  # Deletion protection prevents accidental terraform destroy of the
  # production redirect table. Staging and sandbox tables can be destroyed.
  deletion_protection_enabled = var.environment == "prod"

  attribute {
    name = "PK"
    type = "S"
  }

  attribute {
    name = "SK"
    type = "S"
  }

  attribute {
    name = "GSI1PK"
    type = "S"
  }

  attribute {
    name = "GSI1SK"
    type = "S"
  }

  global_secondary_index {
    name            = "GSI1"
    projection_type = "ALL"

    key_schema {
      attribute_name = "GSI1PK"
      key_type       = "HASH"
    }

    key_schema {
      attribute_name = "GSI1SK"
      key_type       = "RANGE"
    }
  }

  tags = local.common_tags
}
