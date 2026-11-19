resource "aws_secretsmanager_secret" "application_database" {
  name                    = "order-management/dev/database"
  description             = "Database credentials"
  recovery_window_in_days = 7

  tags = {
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}