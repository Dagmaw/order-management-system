resource "aws_ecs_cluster" "main" {
  name = "order-management-dev"

  setting {
    name  = "containerInsights"
    value = "disabled"
  }

  tags = {
    Name        = "order-management-dev"
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_cloudwatch_log_group" "database_setup" {
  name              = "/ecs/order-management-dev/database-setup"
  retention_in_days = 7

  tags = {
    Name        = "order-management-dev-database-setup"
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

output "ecs_cluster_name" {
  description = "ECS cluster used for development tasks and services."
  value       = aws_ecs_cluster.main.name
}