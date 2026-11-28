locals {
  api_image = "638500336727.dkr.ecr.us-east-1.amazonaws.com/order-management-api@sha256:105e39a02d0f7975822e7ea3b1d50c9460c82c490580bf8bf2dbe6666bf432c4"
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

      environment = concat([
        { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },
        { name = "ApiDocs__Enabled", value = "true" },
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
      ], local.orders_inventory_environment)

      secrets = concat([
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
      ], local.orders_inventory_secrets)

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

  lifecycle {
    precondition {
      condition = !var.orders_inventory_enabled || (
        var.inventory_image != "" &&
        local.api_image != "638500336727.dkr.ecr.us-east-1.amazonaws.com/order-management-api@sha256:5d7ac5807863bbc0b651561413407ed6a367d3c87e53b299c4aa08b43a0719f6"
      )
      error_message = "Enable Inventory only after Java activation and pinning the reviewed API release digest."
    }
  }

  depends_on = [aws_iam_role_policy.api_execution, aws_iam_role_policy.api_inventory_identity]

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
