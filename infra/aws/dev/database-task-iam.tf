data "aws_iam_policy_document" "database_task_trust" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }

    condition {
      test     = "StringEquals"
      variable = "aws:SourceAccount"
      values   = ["638500336727"]
    }

    condition {
      test     = "ArnLike"
      variable = "aws:SourceArn"
      values   = ["arn:aws:ecs:us-east-1:638500336727:*"]
    }
  }
}

resource "aws_iam_role" "database_setup_execution" {
  name               = "order-management-dev-db-setup-execution"
  assume_role_policy = data.aws_iam_policy_document.database_task_trust.json

  tags = {
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_iam_role" "database_setup_task" {
  name               = "order-management-dev-db-setup-task"
  assume_role_policy = data.aws_iam_policy_document.database_task_trust.json

  tags = {
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_iam_role_policy" "database_setup_execution" {
  name = "database-setup-execution"
  role = aws_iam_role.database_setup_execution.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid      = "AuthenticateToEcr"
        Effect   = "Allow"
        Action   = "ecr:GetAuthorizationToken"
        Resource = "*"
      },
      {
        Sid    = "PullProjectImage"
        Effect = "Allow"
        Action = [
          "ecr:BatchCheckLayerAvailability",
          "ecr:GetDownloadUrlForLayer",
          "ecr:BatchGetImage"
        ]
        Resource = aws_ecr_repository.order_management_api.arn
      },
      {
        Sid    = "WriteDatabaseSetupLogs"
        Effect = "Allow"
        Action = [
          "logs:CreateLogStream",
          "logs:PutLogEvents"
        ]
        Resource = "${aws_cloudwatch_log_group.database_setup.arn}:*"
      },
      {
        Sid      = "ReadDatabaseAdminSecret"
        Effect   = "Allow"
        Action   = "secretsmanager:GetSecretValue"
        Resource = aws_db_instance.main.master_user_secret[0].secret_arn
      }
    ]
  })
}