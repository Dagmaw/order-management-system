# Keep blank for foundation-only apply. Set an immutable digest only after DB bootstrap and image publication.
variable "inventory_image" {
  type    = string
  default = ""
  validation {
    condition     = var.inventory_image == "" || can(regex("^638500336727\\.dkr\\.ecr\\.us-east-1\\.amazonaws\\.com/inventory-service@sha256:[a-f0-9]{64}$", var.inventory_image))
    error_message = "Inventory image must be an immutable digest in the project Inventory repository."
  }
}
resource "aws_ecs_task_definition" "inventory" {
  count                    = var.inventory_image == "" ? 0 : 1
  family                   = "order-management-dev-inventory"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "512"
  memory                   = "1024"
  execution_role_arn       = aws_iam_role.inventory_execution.arn
  task_role_arn            = aws_iam_role.inventory_task.arn
  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }
  container_definitions = jsonencode([{
    name         = "inventory", image = var.inventory_image, essential = true,
    portMappings = [{ containerPort = 8080, hostPort = 8080, protocol = "tcp" }],
    environment = [
      { name = "SPRING_PROFILES_ACTIVE", value = "production" },
      { name = "JAVA_TOOL_OPTIONS", value = "-XX:MaxRAMPercentage=65" },
      { name = "INVENTORY_DATABASE_URL", value = "jdbc:postgresql://${aws_db_instance.main.address}:5432/inventory?sslmode=verify-full&sslrootcert=/app/certs/global-bundle.pem" },
      { name = "INVENTORY_OPERATIONS_ENABLED", value = "true" },
      { name = "INVENTORY_JWT_MODE", value = "Cognito" },
      { name = "INVENTORY_JWT_ISSUER", value = local.service_identity_issuer },
      { name = "INVENTORY_JWT_JWK_SET_URI", value = "${local.service_identity_issuer}/.well-known/jwks.json" },
      { name = "INVENTORY_JWT_ALLOWED_CLIENT_IDS", value = aws_cognito_user_pool_client.orders_service.id },
      { name = "INVENTORY_JWT_RESERVATION_SCOPE", value = "inventory/reserve" },
      { name = "INVENTORY_JWT_STOCK_SCOPE", value = "inventory/stock.write" },
      { name = "API_DOCS_ENABLED", value = "false" }
    ],
    secrets = [
      { name = "SPRING_DATASOURCE_USERNAME", valueFrom = "${aws_secretsmanager_secret.inventory_database.arn}:username::" },
      { name = "SPRING_DATASOURCE_PASSWORD", valueFrom = "${aws_secretsmanager_secret.inventory_database.arn}:password::" }
    ],
    logConfiguration = { logDriver = "awslogs", options = { "awslogs-group" = aws_cloudwatch_log_group.inventory.name, "awslogs-region" = "us-east-1", "awslogs-stream-prefix" = "inventory" } }
  }])
  tags = local.inventory_tags
}
resource "aws_ecs_service" "inventory" {
  count                              = var.inventory_image == "" ? 0 : 1
  name                               = "order-management-dev-inventory"
  cluster                            = aws_ecs_cluster.main.id
  task_definition                    = aws_ecs_task_definition.inventory[0].arn
  desired_count                      = 1
  launch_type                        = "FARGATE"
  platform_version                   = "1.4.0"
  deployment_minimum_healthy_percent = 100
  deployment_maximum_percent         = 200
  health_check_grace_period_seconds  = 180
  wait_for_steady_state              = true
  enable_ecs_managed_tags            = true
  propagate_tags                     = "SERVICE"
  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }
  network_configuration {
    subnets          = [aws_subnet.main["app_a"].id, aws_subnet.main["app_b"].id]
    security_groups  = [aws_security_group.inventory.id]
    assign_public_ip = false
  }
  load_balancer {
    target_group_arn = aws_lb_target_group.inventory.arn
    container_name   = "inventory"
    container_port   = 8080
  }
  depends_on = [aws_lb_listener.inventory_https, aws_iam_role_policy.inventory_execution, aws_route.application_outbound,
    aws_vpc_security_group_ingress_rule.inventory_from_alb, aws_vpc_security_group_ingress_rule.database_from_inventory,
    aws_vpc_security_group_egress_rule.inventory_to_database, aws_vpc_security_group_egress_rule.inventory_https,
  aws_vpc_security_group_egress_rule.inventory_alb_to_task]
  timeouts {
    create = "20m"
    update = "20m"
    delete = "20m"
  }
  tags = local.inventory_tags
}
