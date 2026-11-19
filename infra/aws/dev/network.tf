resource "aws_vpc" "main" {
  cidr_block           = "10.20.0.0/16"
  enable_dns_support   = true
  enable_dns_hostnames = true

  tags = {
    Name        = "order-management-dev-vpc"
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

output "vpc_id" {
  description = "VPC ID for the development environment."
  value       = aws_vpc.main.id
}