locals {
  database_setup_image = "638500336727.dkr.ecr.us-east-1.amazonaws.com/order-management-api@sha256:e6bb9ada731b592cc8b7f467965ce02fba82b8f3c82b0b87726d57fcfbc0ab18"
}

resource "aws_ecs_task_definition" "database_setup" {
  family                   = "order-management-dev-database-setup"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"

  execution_role_arn = aws_iam_role.database_setup_execution.arn
  task_role_arn      = aws_iam_role.database_setup_task.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  container_definitions = jsonencode([
    {
      name      = "database-setup"
      image     = local.database_setup_image
      essential = true

      environment = [
        {
          name  = "PGHOST"
          value = aws_db_instance.main.address
        },
        {
          name  = "PGDATABASE"
          value = aws_db_instance.main.db_name
        },
        {
          name  = "PGSSLROOTCERT"
          value = "/app/certs/global-bundle.pem"
        }
      ]

      secrets = [
        {
          name      = "PGUSER"
          valueFrom = "${aws_db_instance.main.master_user_secret[0].secret_arn}:username::"
        },
        {
          name      = "PGPASSWORD"
          valueFrom = "${aws_db_instance.main.master_user_secret[0].secret_arn}:password::"
        },
        {
          name      = "PGAPPUSER"
          valueFrom = "${aws_secretsmanager_secret.application_database.arn}:username::"
        },
        {
          name      = "PGAPPPASSWORD"
          valueFrom = "${aws_secretsmanager_secret.application_database.arn}:password::"
        }
      ]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = aws_cloudwatch_log_group.database_setup.name
          awslogs-region        = "us-east-1"
          awslogs-stream-prefix = "migration"
        }
      }
    }
  ])

  depends_on = [
    aws_iam_role_policy.database_setup_execution
  ]

  tags = {
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

output "database_setup_task_definition_arn" {
  description = "Task definition ARN for the database migration task."
  value       = aws_ecs_task_definition.database_setup.arn
}