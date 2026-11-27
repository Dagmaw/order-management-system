resource "aws_iam_role" "api_execution" {
  name               = "order-management-dev-api-execution"
  assume_role_policy = data.aws_iam_policy_document.database_task_trust.json

  tags = {
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_iam_role" "api_task" {
  name               = "order-management-dev-api-task"
  assume_role_policy = data.aws_iam_policy_document.database_task_trust.json

  tags = {
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_iam_role_policy" "api_execution" {
  name = "api-execution"
  role = aws_iam_role.api_execution.id

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
        Sid    = "PullApiImage"
        Effect = "Allow"
        Action = [
          "ecr:BatchCheckLayerAvailability",
          "ecr:GetDownloadUrlForLayer",
          "ecr:BatchGetImage"
        ]
        Resource = aws_ecr_repository.order_management_api.arn
      },
      {
        Sid      = "WriteApiLogs"
        Effect   = "Allow"
        Action   = ["logs:CreateLogStream", "logs:PutLogEvents"]
        Resource = "${aws_cloudwatch_log_group.api.arn}:*"
      },
      {
        Sid    = "ReadApiSecrets"
        Effect = "Allow"
        Action = "secretsmanager:GetSecretValue"
        Resource = [
          aws_secretsmanager_secret.application_database.arn,
          aws_secretsmanager_secret.api_jwt.arn
        ]
      }
    ]
  })
}
