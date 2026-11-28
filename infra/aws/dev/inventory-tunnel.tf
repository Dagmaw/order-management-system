locals {
  inventory_tunnel_tags = merge(local.inventory_tags, { Name = "order-management-dev-inventory-tunnel", Purpose = "swagger-tunnel" })
}
data "aws_ami" "inventory_tunnel" {
  owners = ["amazon"]
  filter {
    name   = "image-id"
    values = ["ami-0358f644d7299878d"]
  }
  filter {
    name   = "architecture"
    values = ["arm64"]
  }
}
resource "aws_security_group" "inventory_tunnel" {
  name        = "order-management-dev-inventory-tunnel"
  description = "SSM tunnel helper; no inbound access or SSH"
  vpc_id      = aws_vpc.main.id
  tags        = local.inventory_tunnel_tags
}
resource "aws_vpc_security_group_egress_rule" "inventory_tunnel_https" {
  security_group_id = aws_security_group.inventory_tunnel.id
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "tcp"
  from_port         = 443
  to_port           = 443
}
resource "aws_vpc_security_group_ingress_rule" "inventory_alb_from_tunnel" {
  security_group_id            = aws_security_group.inventory_alb.id
  referenced_security_group_id = aws_security_group.inventory_tunnel.id
  ip_protocol                  = "tcp"
  from_port                    = 443
  to_port                      = 443
}
resource "aws_iam_role" "inventory_tunnel" {
  name = "order-management-dev-inventory-tunnel"
  assume_role_policy = jsonencode({ Version = "2012-10-17", Statement = [{
    Effect = "Allow", Action = "sts:AssumeRole", Principal = { Service = "ec2.amazonaws.com" }
  }] })
  tags = local.inventory_tunnel_tags
}
resource "aws_iam_role_policy_attachment" "inventory_tunnel_ssm" {
  role       = aws_iam_role.inventory_tunnel.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}
resource "aws_iam_instance_profile" "inventory_tunnel" {
  name = "order-management-dev-inventory-tunnel"
  role = aws_iam_role.inventory_tunnel.name
  tags = local.inventory_tunnel_tags
}
resource "aws_instance" "inventory_tunnel" {
  ami                                  = data.aws_ami.inventory_tunnel.id
  instance_type                        = "t4g.micro"
  subnet_id                            = aws_subnet.main["app_a"].id
  associate_public_ip_address          = false
  vpc_security_group_ids               = [aws_security_group.inventory_tunnel.id]
  iam_instance_profile                 = aws_iam_instance_profile.inventory_tunnel.name
  user_data                            = "#!/bin/bash\nset -eu\nsystemctl enable --now amazon-ssm-agent\n"
  user_data_replace_on_change          = true
  instance_initiated_shutdown_behavior = "stop"
  metadata_options {
    http_endpoint               = "enabled"
    http_tokens                 = "required"
    http_put_response_hop_limit = 1
  }
  credit_specification { cpu_credits = "standard" }
  root_block_device {
    volume_type           = "gp3"
    volume_size           = 8
    encrypted             = true
    delete_on_termination = true
  }
  tags        = local.inventory_tunnel_tags
  volume_tags = local.inventory_tunnel_tags
  depends_on  = [aws_iam_role_policy_attachment.inventory_tunnel_ssm, aws_route.application_outbound]
}
resource "aws_ssm_document" "inventory_swagger_forward" {
  name            = "order-management-dev-inventory-swagger-forward"
  document_type   = "Session"
  document_format = "JSON"
  content = jsonencode({
    schemaVersion = "1.0", description = "Forward only to private Inventory HTTPS; no shell or arbitrary host",
    sessionType = "Port", properties = {
      host = local.inventory_hostname, portNumber = "443", localPortNumber = "8443", type = "LocalPortForwarding"
    }
  })
  tags = local.inventory_tunnel_tags
}
resource "aws_iam_role" "inventory_tunnel_operator" {
  name = "order-management-dev-inventory-tunnel-operator"
  assume_role_policy = jsonencode({ Version = "2012-10-17", Statement = [{
    Effect    = "Allow", Action = "sts:AssumeRole",
    Principal = { AWS = "arn:aws:iam::638500336727:user/dtesfa2022" }
  }] })
  tags = local.inventory_tunnel_tags
}
resource "aws_iam_role_policy" "inventory_tunnel_operator" {
  name = "inventory-swagger-tunnel-only"
  role = aws_iam_role.inventory_tunnel_operator.id
  policy = jsonencode({ Version = "2012-10-17", Statement = [
    { Effect = "Allow", Action = "ssm:StartSession", Resource = [aws_instance.inventory_tunnel.arn, aws_ssm_document.inventory_swagger_forward.arn],
    Condition = { BoolIfExists = { "ssm:SessionDocumentAccessCheck" = "true" } } },
    { Effect   = "Allow", Action = ["ssm:TerminateSession", "ssm:ResumeSession", "ssmmessages:OpenDataChannel"],
      Resource = "arn:aws:ssm:us-east-1:638500336727:session/*",
    Condition = { StringEquals = { "ssm:resourceTag/aws:ssmmessages:session-id" = "$${aws:userid}" } } },
    { Effect = "Allow", Action = ["ec2:StartInstances", "ec2:StopInstances"], Resource = aws_instance.inventory_tunnel.arn },
    { Effect = "Allow", Action = ["ec2:DescribeInstances", "ssm:DescribeInstanceInformation", "ssm:GetConnectionStatus"], Resource = "*",
    Condition = { StringEquals = { "aws:RequestedRegion" = "us-east-1" } } }
  ] })
}
output "inventory_swagger_tunnel" {
  description = "Non-secret helper and fixed session information for local access."
  value = {
    instance_id       = aws_instance.inventory_tunnel.id
    operator_role_arn = aws_iam_role.inventory_tunnel_operator.arn
    session_document  = aws_ssm_document.inventory_swagger_forward.name
    hostname          = local.inventory_hostname
    browser_url       = "https://${local.inventory_hostname}/swagger-ui/index.html"
  }
}
