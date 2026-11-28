locals {
  inventory_tags     = { Project = "order-management-system", Environment = "dev", ManagedBy = "Terraform", Component = "inventory" }
  inventory_hostname = "inventory.dev.internal.kaddigitalsolutions.com"
}

resource "aws_ecr_repository" "inventory" {
  name                 = "inventory-service"
  image_tag_mutability = "IMMUTABLE"
  force_delete         = false
  image_scanning_configuration { scan_on_push = true }
  tags = local.inventory_tags
}
resource "aws_ecr_lifecycle_policy" "inventory" {
  repository = aws_ecr_repository.inventory.name
  policy     = jsonencode({ rules = [{ rulePriority = 1, description = "Expire only untagged images after seven days", selection = { tagStatus = "untagged", countType = "sinceImagePushed", countUnit = "days", countNumber = 7 }, action = { type = "expire" } }] })
}
resource "aws_secretsmanager_secret" "inventory_database" {
  name                    = "order-management/dev/inventory-database"
  description             = "Dedicated Inventory PostgreSQL login; initialized by approved workflow"
  recovery_window_in_days = 7
  tags                    = local.inventory_tags
}
resource "aws_cloudwatch_log_group" "inventory" {
  name              = "/ecs/order-management-dev/inventory"
  retention_in_days = 7
  tags              = local.inventory_tags
}
resource "aws_cloudwatch_log_group" "inventory_setup" {
  name              = "/ecs/order-management-dev/inventory-setup"
  retention_in_days = 7
  tags              = local.inventory_tags
}
resource "aws_security_group" "inventory" {
  name        = "order-management-dev-inventory"
  description = "Private Inventory tasks"
  vpc_id      = aws_vpc.main.id
  tags        = local.inventory_tags
}
resource "aws_security_group" "inventory_alb" {
  name        = "order-management-dev-inventory-alb"
  description = "Private HTTPS entry point for Inventory"
  vpc_id      = aws_vpc.main.id
  tags        = local.inventory_tags
}
resource "aws_vpc_security_group_ingress_rule" "inventory_alb_from_api" {
  security_group_id            = aws_security_group.inventory_alb.id
  referenced_security_group_id = aws_security_group.api.id
  ip_protocol                  = "tcp"
  from_port                    = 443
  to_port                      = 443
}
resource "aws_vpc_security_group_egress_rule" "inventory_alb_to_task" {
  security_group_id            = aws_security_group.inventory_alb.id
  referenced_security_group_id = aws_security_group.inventory.id
  ip_protocol                  = "tcp"
  from_port                    = 8080
  to_port                      = 8080
}
resource "aws_vpc_security_group_ingress_rule" "inventory_from_alb" {
  security_group_id            = aws_security_group.inventory.id
  referenced_security_group_id = aws_security_group.inventory_alb.id
  ip_protocol                  = "tcp"
  from_port                    = 8080
  to_port                      = 8080
}
resource "aws_vpc_security_group_egress_rule" "inventory_to_database" {
  security_group_id            = aws_security_group.inventory.id
  referenced_security_group_id = aws_security_group.database.id
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
}
resource "aws_vpc_security_group_ingress_rule" "database_from_inventory" {
  security_group_id            = aws_security_group.database.id
  referenced_security_group_id = aws_security_group.inventory.id
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
}
resource "aws_vpc_security_group_egress_rule" "inventory_https" {
  security_group_id = aws_security_group.inventory.id
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "tcp"
  from_port         = 443
  to_port           = 443
}
resource "aws_route53_zone" "inventory_private" {
  name = "internal.kaddigitalsolutions.com"
  vpc { vpc_id = aws_vpc.main.id }
  tags = local.inventory_tags
}
resource "aws_acm_certificate" "inventory" {
  domain_name       = local.inventory_hostname
  validation_method = "DNS"
  lifecycle { create_before_destroy = true }
  tags = local.inventory_tags
}
resource "aws_route53_record" "inventory_certificate" {
  for_each = { for item in aws_acm_certificate.inventory.domain_validation_options : item.domain_name => item }
  zone_id  = data.aws_route53_zone.company.zone_id
  name     = each.value.resource_record_name
  type     = each.value.resource_record_type
  records  = [each.value.resource_record_value]
  ttl      = 300
}
resource "aws_acm_certificate_validation" "inventory" {
  certificate_arn         = aws_acm_certificate.inventory.arn
  validation_record_fqdns = [for item in aws_route53_record.inventory_certificate : item.fqdn]
}
resource "aws_lb" "inventory" {
  name                       = "order-management-dev-inventory"
  internal                   = true
  load_balancer_type         = "application"
  security_groups            = [aws_security_group.inventory_alb.id]
  subnets                    = [aws_subnet.main["app_a"].id, aws_subnet.main["app_b"].id]
  drop_invalid_header_fields = true
  tags                       = local.inventory_tags
}
resource "aws_lb_target_group" "inventory" {
  name_prefix          = "ominv-"
  port                 = 8080
  protocol             = "HTTP"
  target_type          = "ip"
  vpc_id               = aws_vpc.main.id
  deregistration_delay = 30
  health_check {
    path                = "/actuator/health/readiness"
    matcher             = "200"
    interval            = 30
    timeout             = 5
    healthy_threshold   = 2
    unhealthy_threshold = 3
  }
  lifecycle { create_before_destroy = true }
  tags = local.inventory_tags
}
resource "aws_lb_listener" "inventory_https" {
  load_balancer_arn = aws_lb.inventory.arn
  port              = 443
  protocol          = "HTTPS"
  ssl_policy        = "ELBSecurityPolicy-TLS13-1-2-2021-06"
  certificate_arn   = aws_acm_certificate_validation.inventory.certificate_arn
  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.inventory.arn
  }
  tags = local.inventory_tags
}
resource "aws_route53_record" "inventory_private" {
  zone_id = aws_route53_zone.inventory_private.zone_id
  name    = local.inventory_hostname
  type    = "A"
  alias {
    name                   = aws_lb.inventory.dns_name
    zone_id                = aws_lb.inventory.zone_id
    evaluate_target_health = true
  }
}
output "inventory_foundation" {
  value = {
    base_url               = "https://${local.inventory_hostname}/"
    repository_url         = aws_ecr_repository.inventory.repository_url
    database_secret_arn    = aws_secretsmanager_secret.inventory_database.arn
    task_security_group_id = aws_security_group.inventory.id
    target_group_arn       = aws_lb_target_group.inventory.arn
  }
}
