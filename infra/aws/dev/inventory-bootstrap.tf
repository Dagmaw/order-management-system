# One-off bootstrap only. The continuously running Inventory service remains disabled.
locals {
  inventory_bootstrap_image = "638500336727.dkr.ecr.us-east-1.amazonaws.com/order-management-api@sha256:e25a934e7816c06da46b5e2c05b343925bc176db8c1f4287e1da89fb9a1415c8"
}
resource "aws_iam_role" "inventory_bootstrap_execution" {
  name               = "order-management-dev-inventory-bootstrap-execution"
  assume_role_policy = data.aws_iam_policy_document.database_task_trust.json
  tags               = local.inventory_tags
}
resource "aws_iam_role" "inventory_bootstrap_task" {
  name               = "order-management-dev-inventory-bootstrap-task"
  assume_role_policy = data.aws_iam_policy_document.database_task_trust.json
  tags               = local.inventory_tags
}
resource "aws_iam_role_policy" "inventory_bootstrap_execution" {
  name = "inventory-bootstrap-execution"
  role = aws_iam_role.inventory_bootstrap_execution.id
  policy = jsonencode({ Version = "2012-10-17", Statement = [
    { Effect = "Allow", Action = "ecr:GetAuthorizationToken", Resource = "*" },
    { Effect = "Allow", Action = ["ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"], Resource = aws_ecr_repository.order_management_api.arn },
    { Effect = "Allow", Action = ["logs:CreateLogStream", "logs:PutLogEvents"], Resource = "${aws_cloudwatch_log_group.inventory_setup.arn}:*" },
    { Effect = "Allow", Action = "secretsmanager:GetSecretValue", Resource = [aws_db_instance.main.master_user_secret[0].secret_arn, aws_secretsmanager_secret.inventory_database.arn] }
  ] })
}
resource "aws_ecs_task_definition" "inventory_bootstrap" {
  family                   = "order-management-dev-inventory-bootstrap"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = aws_iam_role.inventory_bootstrap_execution.arn
  task_role_arn            = aws_iam_role.inventory_bootstrap_task.arn
  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }
  container_definitions = jsonencode([{
    name = "inventory-bootstrap", image = local.inventory_bootstrap_image, essential = true,
    environment = [
      { name = "DB_SETUP_OPERATION", value = "initialize-inventory" },
      { name = "PGHOST", value = aws_db_instance.main.address },
      { name = "PGDATABASE", value = "postgres" },
      { name = "PGSSLROOTCERT", value = "/app/certs/global-bundle.pem" }
    ],
    secrets = [
      { name = "PGUSER", valueFrom = "${aws_db_instance.main.master_user_secret[0].secret_arn}:username::" },
      { name = "PGPASSWORD", valueFrom = "${aws_db_instance.main.master_user_secret[0].secret_arn}:password::" },
      { name = "PGAPPUSER", valueFrom = "${aws_secretsmanager_secret.inventory_database.arn}:username::" },
      { name = "PGAPPPASSWORD", valueFrom = "${aws_secretsmanager_secret.inventory_database.arn}:password::" }
    ],
    logConfiguration = { logDriver = "awslogs", options = {
      awslogs-group = aws_cloudwatch_log_group.inventory_setup.name, awslogs-region = "us-east-1", awslogs-stream-prefix = "bootstrap"
    } }
  }])
  depends_on = [aws_iam_role_policy.inventory_bootstrap_execution]
  lifecycle {
    precondition {
      condition     = can(regex("^638500336727\\.dkr\\.ecr\\.us-east-1\\.amazonaws\\.com/order-management-api@sha256:[a-f0-9]{64}$", local.inventory_bootstrap_image))
      error_message = "Bootstrap must use a published immutable DatabaseSetup image digest."
    }
  }
  tags = local.inventory_tags
}
resource "aws_iam_role" "inventory_bootstrap_workflow" {
  name = "order-management-dev-inventory-bootstrap-workflow"
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
resource "aws_iam_role_policy" "inventory_bootstrap_workflow" {
  name = "inventory-bootstrap-workflow"
  role = aws_iam_role.inventory_bootstrap_workflow.id
  policy = jsonencode({ Version = "2012-10-17", Statement = [
    { Effect = "Allow", Action = "ecs:RunTask", Resource = aws_ecs_task_definition.inventory_bootstrap.arn,
    Condition = { ArnEquals = { "ecs:cluster" = aws_ecs_cluster.main.arn } } },
    { Effect = "Allow", Action = "ecs:DescribeTasks", Resource = "arn:aws:ecs:us-east-1:638500336727:task/order-management-dev/*" },
    { Effect = "Allow", Action = "iam:PassRole", Resource = [aws_iam_role.inventory_bootstrap_execution.arn, aws_iam_role.inventory_bootstrap_task.arn],
    Condition = { StringEquals = { "iam:PassedToService" = "ecs-tasks.amazonaws.com" } } }
  ] })
}
output "inventory_bootstrap" {
  value = {
    workflow_role_arn   = aws_iam_role.inventory_bootstrap_workflow.arn
    task_definition_arn = aws_ecs_task_definition.inventory_bootstrap.arn
    subnet_ids          = join(",", [aws_subnet.main["app_a"].id, aws_subnet.main["app_b"].id])
    security_group_id   = aws_security_group.inventory.id
  }
}
