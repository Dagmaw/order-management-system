locals {
  api_image = "638500336727.dkr.ecr.us-east-1.amazonaws.com/order-management-api@sha256:eaec27e5e965a4d030dc4ce038d793c9e28b09ae17b482d048e96ee4be1e0b7c"
}

resource "aws_cloudwatch_log_group" "api" {
  name              = "/ecs/order-management-dev/api"
  retention_in_days = 7

  tags = {
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_ecs_task_definition" "api" {
  family                   = "order-management-dev-api"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = aws_iam_role.api_execution.arn
  task_role_arn            = aws_iam_role.api_task.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  container_definitions = jsonencode([
    {
      name      = "api"
      image     = local.api_image
      essential = true

      portMappings = [
        {
          containerPort = 8080
          hostPort      = 8080
          protocol      = "tcp"
        }
      ]

      environment = [
        { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },
        { name = "ASPNETCORE_HTTP_PORTS", value = "8080" },
        { name = "ASPNETCORE_HTTPS_PORT", value = "443" },
        { name = "Database__UseEnvironmentVariables", value = "true" },
        { name = "PGHOST", value = aws_db_instance.main.address },
        { name = "PGDATABASE", value = aws_db_instance.main.db_name },
        { name = "PGSSLROOTCERT", value = "/app/certs/global-bundle.pem" },
        { name = "Jwt__Issuer", value = "OrderManagement.API" },
        { name = "Jwt__Audience", value = "OrderManagement.Client" },
        { name = "Jwt__AccessTokenMinutes", value = "15" },
        { name = "BootstrapAdmin__Enabled", value = "false" },
        { name = "ReverseProxy__Enabled", value = "true" },
        { name = "ReverseProxy__KnownNetworks__0", value = aws_subnet.main["public_a"].cidr_block },
        { name = "ReverseProxy__KnownNetworks__1", value = aws_subnet.main["public_b"].cidr_block }
      ]

      secrets = [
        {
          name      = "PGUSER"
          valueFrom = "${aws_secretsmanager_secret.application_database.arn}:username::"
        },
        {
          name      = "PGPASSWORD"
          valueFrom = "${aws_secretsmanager_secret.application_database.arn}:password::"
        },
        {
          name      = "Jwt__SigningKeyBase64"
          valueFrom = aws_secretsmanager_secret.api_jwt.arn
        }
      ]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = aws_cloudwatch_log_group.api.name
          awslogs-region        = "us-east-1"
          awslogs-stream-prefix = "api"
        }
      }
    }
  ])

  depends_on = [aws_iam_role_policy.api_execution]

  tags = {
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

output "api_task_definition_arn" {
  description = "Task definition ARN for the Order Management API."
  value       = aws_ecs_task_definition.api.arn
}
