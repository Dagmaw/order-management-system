resource "aws_secretsmanager_secret" "api_jwt" {
  name                    = "order-management/dev/jwt"
  description             = "JWT signing key for the Order Management API"
  recovery_window_in_days = 7

  tags = {
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}