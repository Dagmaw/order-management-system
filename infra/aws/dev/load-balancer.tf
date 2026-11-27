resource "aws_lb" "api" {
  name               = "order-management-dev-api"
  internal           = false
  load_balancer_type = "application"
  ip_address_type    = "ipv4"
  security_groups    = [aws_security_group.alb.id]
  subnets            = [
    aws_subnet.main["public_a"].id,
    aws_subnet.main["public_b"].id
  ]

  drop_invalid_header_fields = true
  enable_deletion_protection = false

  depends_on = [aws_route.public_internet]

  tags = {
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_lb_target_group" "api" {
  name_prefix          = "omapi-"
  port                 = 8080
  protocol             = "HTTP"
  target_type          = "ip"
  vpc_id               = aws_vpc.main.id
  deregistration_delay = 30

  health_check {
    enabled             = true
    path                = "/health"
    port                = "traffic-port"
    protocol            = "HTTP"
    matcher             = "200"
    interval            = 30
    timeout             = 5
    healthy_threshold   = 2
    unhealthy_threshold = 3
  }

  lifecycle {
    create_before_destroy = true
  }

  tags = {
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_lb_listener" "api_https" {
  load_balancer_arn = aws_lb.api.arn
  port              = 443
  protocol          = "HTTPS"
  ssl_policy        = "ELBSecurityPolicy-TLS13-1-2-2021-06"
  certificate_arn   = aws_acm_certificate_validation.api.certificate_arn

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.api.arn
  }

  tags = {
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_lb_listener" "api_http" {
  load_balancer_arn = aws_lb.api.arn
  port              = 80
  protocol          = "HTTP"

  default_action {
    type = "redirect"

    redirect {
      host        = local.api_hostname
      port        = "443"
      protocol    = "HTTPS"
      status_code = "HTTP_301"
    }
  }

  tags = {
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_vpc_security_group_ingress_rule" "alb_http" {
  security_group_id = aws_security_group.alb.id

  description = "HTTP requests redirected to HTTPS by the load balancer"
  cidr_ipv4   = "0.0.0.0/0"
  ip_protocol = "tcp"
  from_port   = 80
  to_port     = 80
}

resource "aws_route53_record" "api" {
  zone_id = data.aws_route53_zone.company.zone_id
  name    = local.api_hostname
  type    = "A"

  alias {
    name                   = aws_lb.api.dns_name
    zone_id                = aws_lb.api.zone_id
    evaluate_target_health = true
  }
}

output "api_url" {
  description = "Public HTTPS URL for the development API."
  value       = "https://${local.api_hostname}"
}

output "api_load_balancer_dns" {
  description = "AWS-generated DNS name of the API load balancer."
  value       = aws_lb.api.dns_name
}
