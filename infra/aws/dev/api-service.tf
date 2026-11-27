resource "aws_ecs_service" "api" {
  name             = "order-management-dev-api"
  cluster          = aws_ecs_cluster.main.id
  task_definition  = aws_ecs_task_definition.api.arn
  desired_count    = 1
  launch_type      = "FARGATE"
  platform_version = "1.4.0"

  deployment_minimum_healthy_percent = 100
  deployment_maximum_percent         = 200
  health_check_grace_period_seconds  = 120
  wait_for_steady_state              = true
  enable_ecs_managed_tags            = true
  propagate_tags                     = "SERVICE"

  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  network_configuration {
    subnets = [
      aws_subnet.main["app_a"].id,
      aws_subnet.main["app_b"].id
    ]
    security_groups  = [aws_security_group.api.id]
    assign_public_ip = false
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = "api"
    container_port   = 8080
  }

  depends_on = [
    aws_lb_listener.api_https,
    aws_lb_listener.api_http,
    aws_vpc_security_group_ingress_rule.alb_http,
    aws_vpc_security_group_ingress_rule.api_from_alb,
    aws_vpc_security_group_ingress_rule.database_from_api,
    aws_vpc_security_group_egress_rule.api_to_database,
    aws_vpc_security_group_egress_rule.api_https,
    aws_vpc_security_group_egress_rule.alb_to_api,
    aws_route.application_outbound
  ]

  timeouts {
    create = "20m"
    update = "20m"
    delete = "20m"
  }

  tags = {
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

output "api_service_name" {
  description = "ECS service running the development API."
  value       = aws_ecs_service.api.name
}
