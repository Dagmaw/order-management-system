# One-off verification; no credentials, database permissions, or running service.
locals {
  inventory_verification_image = "638500336727.dkr.ecr.us-east-1.amazonaws.com/order-management-api@sha256:66279c18e711dd9b6b21028b3d8be289659121e3d348a10fea75811ff445c222"
}
resource "aws_iam_role" "inventory_verification_execution" {
  name               = "order-management-dev-inventory-verification-execution"
  assume_role_policy = data.aws_iam_policy_document.database_task_trust.json
  tags               = local.inventory_tags
}
resource "aws_iam_role" "inventory_verification_task" {
  name               = "order-management-dev-inventory-verification-task"
  assume_role_policy = data.aws_iam_policy_document.database_task_trust.json
  tags               = local.inventory_tags
}
resource "aws_iam_role_policy" "inventory_verification_execution" {
  name = "inventory-verification-execution"
  role = aws_iam_role.inventory_verification_execution.id
  policy = jsonencode({ Version = "2012-10-17", Statement = [
    { Effect = "Allow", Action = "ecr:GetAuthorizationToken", Resource = "*" },
    { Effect = "Allow", Action = ["ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"], Resource = aws_ecr_repository.order_management_api.arn },
    { Effect = "Allow", Action = ["logs:CreateLogStream", "logs:PutLogEvents"], Resource = "${aws_cloudwatch_log_group.inventory_verification.arn}:*" }
  ] })
}
resource "aws_ecs_task_definition" "inventory_verification" {
  family                   = "order-management-dev-inventory-verification"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = aws_iam_role.inventory_verification_execution.arn
  task_role_arn            = aws_iam_role.inventory_verification_task.arn
  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }
  container_definitions = jsonencode([{
    name = "inventory-verification", image = local.inventory_verification_image, essential = true,
    logConfiguration = { logDriver = "awslogs", options = {
      awslogs-group = aws_cloudwatch_log_group.inventory_verification.name, awslogs-region = "us-east-1", awslogs-stream-prefix = "verification"
    } }
  }])
  depends_on = [aws_iam_role_policy.inventory_verification_execution]
  lifecycle {
    precondition {
      condition     = can(regex("^638500336727\\.dkr\\.ecr\\.us-east-1\\.amazonaws\\.com/order-management-api@sha256:[a-f0-9]{64}$", local.inventory_verification_image))
      error_message = "Verification must use the published immutable verifier image digest."
    }
  }
  tags = local.inventory_tags
}
resource "aws_iam_role" "inventory_verification_workflow" {
  name = "order-management-dev-inventory-verification-workflow"
  assume_role_policy = jsonencode({ Version = "2012-10-17", Statement = [{
    Effect    = "Allow", Action = "sts:AssumeRoleWithWebIdentity",
    Principal = { Federated = "arn:aws:iam::638500336727:oidc-provider/token.actions.githubusercontent.com" },
    Condition = { StringEquals = {
      "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com",
      "token.actions.githubusercontent.com:sub" = "repo:Dagmaw@31310623/order-management-system@1431786532:environment:terraform-dev"
    } }
  }] })
  tags = local.inventory_tags
}
resource "aws_iam_role_policy" "inventory_verification_workflow" {
  name = "inventory-verification-workflow"
  role = aws_iam_role.inventory_verification_workflow.id
  policy = jsonencode({ Version = "2012-10-17", Statement = [
    { Effect = "Allow", Action = "ecs:RunTask", Resource = aws_ecs_task_definition.inventory_verification.arn,
    Condition = { ArnEquals = { "ecs:cluster" = aws_ecs_cluster.main.arn } } },
    { Effect = "Allow", Action = "ecs:DescribeTasks", Resource = "arn:aws:ecs:us-east-1:638500336727:task/order-management-dev/*" },
    { Effect = "Allow", Action = "logs:GetLogEvents", Resource = "${aws_cloudwatch_log_group.inventory_verification.arn}:log-stream:verification/*" },
    { Effect = "Allow", Action = "iam:PassRole", Resource = [aws_iam_role.inventory_verification_execution.arn, aws_iam_role.inventory_verification_task.arn],
    Condition = { StringEquals = { "iam:PassedToService" = "ecs-tasks.amazonaws.com" } } }
  ] })
}
output "inventory_verification" {
  value = {
    workflow_role_arn   = aws_iam_role.inventory_verification_workflow.arn
    task_definition_arn = aws_ecs_task_definition.inventory_verification.arn
    subnet_ids          = join(",", [aws_subnet.main["app_a"].id, aws_subnet.main["app_b"].id])
    security_group_id   = aws_security_group.inventory_verification.id
  }
}

resource "aws_cloudwatch_log_group" "inventory_verification" {
  name              = "/ecs/order-management-dev/inventory-verification"
  retention_in_days = 7
  tags              = local.inventory_tags
}
resource "aws_security_group" "inventory_verification" {
  name        = "order-management-dev-inventory-verification"
  description = "One-off stock verifier; HTTPS only, no database access"
  vpc_id      = aws_vpc.main.id
  tags        = local.inventory_tags
}
resource "aws_vpc_security_group_egress_rule" "inventory_verification_https" {
  security_group_id = aws_security_group.inventory_verification.id
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "tcp"
  from_port         = 443
  to_port           = 443
}
resource "aws_vpc_security_group_ingress_rule" "inventory_alb_from_verification" {
  security_group_id            = aws_security_group.inventory_alb.id
  referenced_security_group_id = aws_security_group.inventory_verification.id
  ip_protocol                  = "tcp"
  from_port                    = 443
  to_port                      = 443
}
