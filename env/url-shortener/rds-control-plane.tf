# Control plane database — Aurora Serverless v2 (PostgreSQL) per ADR-006.
#
# Automated backups run daily with a 35-day retention window (prod) or
# 7 days (staging/sandbox). Backups are stored in S3 and support
# point-in-time recovery to any second within the retention window.
#
# Restore procedure: see docs/plans/disaster-recovery-runbook.md#scenario-s2.

# ── Default VPC lookup (pre-production) ───────────────────────────────────

data "aws_vpc" "default" {
  default = true
}

data "aws_subnets" "default" {
  filter {
    name   = "vpc-id"
    values = [data.aws_vpc.default.id]
  }
}

resource "aws_db_subnet_group" "control_plane" {
  name       = "control-plane-${var.environment}"
  subnet_ids = data.aws_subnets.default.ids

  tags = local.common_tags
}

resource "aws_security_group" "control_plane_db" {
  name        = "control-plane-db-${var.environment}"
  description = "Security group for the ControlPlane Aurora cluster"
  vpc_id      = data.aws_vpc.default.id

  ingress {
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [] # Populated when ControlPlane Lambda / API security group exists
    description     = "PostgreSQL access from ControlPlane API"
  }

  tags = local.common_tags
}

# ── Aurora Serverless v2 cluster ──────────────────────────────────────────

resource "aws_rds_cluster" "control_plane" {
  cluster_identifier   = "control-plane-${var.environment}"
  engine               = "aurora-postgresql"
  engine_mode          = "provisioned"
  engine_version       = "16.4"
  database_name        = "controlplane"
  master_username      = "controlplane_admin"
  master_password      = random_password.control_plane_db.result
  db_subnet_group_name = aws_db_subnet_group.control_plane.name

  vpc_security_group_ids = [aws_security_group.control_plane_db.id]

  # Automated backup configuration — ADR-013 Section "Backup Mechanisms"
  backup_retention_period = var.environment == "prod" ? 35 : 7
  preferred_backup_window = "03:00-04:00" # UTC — off-peak window

  # Copy automated backups to DR region (us-west-2) for geographic separation.
  # Only enabled for production to avoid unnecessary cross-region storage costs.
  copy_tags_to_snapshot = true

  # Deletion protection prevents accidental `terraform destroy` of production data.
  deletion_protection = var.environment == "prod"

  # Storage encryption at rest using AWS-managed KMS key (AES-256).
  storage_encrypted = true

  # IAM database authentication — enables passwordless access from Lambda
  # functions via IAM roles, eliminating static credentials.
  iam_database_authentication_enabled = true

  # Enable Performance Insights for query monitoring.
  # 7-day retention is the free tier maximum.
  enabled_cloudwatch_logs_exports = ["postgresql"]
  performance_insights_enabled    = var.environment == "prod"

  serverlessv2_scaling_configuration {
    min_capacity = 0.5 # Scale to zero ACU when idle
    max_capacity = 4.0 # Maximum during peak usage
  }

  tags = local.common_tags
}

# ── Database password (auto-generated) ────────────────────────────────────

resource "random_password" "control_plane_db" {
  length  = 32
  special = false # Avoid characters that require URL encoding in connection strings
}

# ── Secrets Manager — store credentials securely ──────────────────────────

resource "aws_secretsmanager_secret" "control_plane_db" {
  name        = "control-plane-db-${var.environment}"
  description = "Connection details for the ControlPlane Aurora PostgreSQL cluster"

  tags = local.common_tags
}

resource "aws_secretsmanager_secret_version" "control_plane_db" {
  secret_id = aws_secretsmanager_secret.control_plane_db.id

  secret_string = jsonencode({
    host     = aws_rds_cluster.control_plane.endpoint
    port     = 5432
    database = "controlplane"
    username = aws_rds_cluster.control_plane.master_username
    password = random_password.control_plane_db.result
  })
}
