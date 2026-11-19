resource "aws_db_subnet_group" "main" {
  name        = "order-management-dev-database"
  description = "Private database subnets for order management"

  subnet_ids = [
    aws_subnet.main["database_a"].id,
    aws_subnet.main["database_b"].id
  ]

  tags = {
    Name        = "order-management-dev-database"
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}