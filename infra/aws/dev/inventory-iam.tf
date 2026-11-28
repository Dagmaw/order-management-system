# Image publishing is a separate manual workflow after foundation provisioning.
resource "aws_iam_role" "inventory_publish" {
  name = "order-management-dev-inventory-publish"
  assume_role_policy = jsonencode({ Version = "2012-10-17", Statement = [{
    Effect    = "Allow", Action = "sts:AssumeRoleWithWebIdentity",
    Principal = { Federated = "arn:aws:iam::638500336727:oidc-provider/token.actions.githubusercontent.com" },
    Condition = { StringEquals = {
      "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com",
      "token.actions.githubusercontent.com:sub" = "repo:Dagmaw@31310623/order-management-system@1431786532:ref:refs/heads/main"
    } }
  }] })
  tags = local.inventory_tags
}
resource "aws_iam_role_policy" "inventory_publish" {
  name = "inventory-publish"
  role = aws_iam_role.inventory_publish.id
  policy = jsonencode({ Version = "2012-10-17", Statement = [
    { Effect = "Allow", Action = "ecr:GetAuthorizationToken", Resource = "*" },
    { Effect = "Allow", Action = ["ecr:BatchCheckLayerAvailability", "ecr:InitiateLayerUpload", "ecr:UploadLayerPart", "ecr:CompleteLayerUpload", "ecr:PutImage", "ecr:BatchGetImage"], Resource = aws_ecr_repository.inventory.arn }
  ] })
}
resource "aws_iam_role" "inventory_execution" {
  name               = "order-management-dev-inventory-execution"
  assume_role_policy = data.aws_iam_policy_document.database_task_trust.json
  tags               = local.inventory_tags
}
resource "aws_iam_role" "inventory_task" {
  name               = "order-management-dev-inventory-task"
  assume_role_policy = data.aws_iam_policy_document.database_task_trust.json
  tags               = local.inventory_tags
}
resource "aws_iam_role_policy" "inventory_execution" {
  name = "inventory-execution"
  role = aws_iam_role.inventory_execution.id
  policy = jsonencode({ Version = "2012-10-17", Statement = [
    { Effect = "Allow", Action = "ecr:GetAuthorizationToken", Resource = "*" },
    { Effect = "Allow", Action = ["ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage"], Resource = aws_ecr_repository.inventory.arn },
    { Effect = "Allow", Action = ["logs:CreateLogStream", "logs:PutLogEvents"], Resource = "${aws_cloudwatch_log_group.inventory.arn}:*" },
    { Effect = "Allow", Action = "secretsmanager:GetSecretValue", Resource = aws_secretsmanager_secret.inventory_database.arn }
  ] })
}

resource "aws_iam_role" "inventory_secret_init" {
  name = "order-management-dev-inventory-secret-init"
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
resource "aws_iam_role_policy" "inventory_secret_init" {
  name = "inventory-secret-init"
  role = aws_iam_role.inventory_secret_init.id
  policy = jsonencode({ Version = "2012-10-17", Statement = [{
    Effect   = "Allow", Action = ["secretsmanager:DescribeSecret", "secretsmanager:PutSecretValue"],
    Resource = aws_secretsmanager_secret.inventory_database.arn
  }] })
}
