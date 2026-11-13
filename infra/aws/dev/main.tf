terraform {
  required_version = "= 1.17.2"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "= 6.73.0"
    }
  }

  backend "s3" {
    bucket         = "order-management-tfstate-dev-638500336727-us-east-1"
    key            = "terraform.tfstate"
    region         = "us-east-1"
    encrypt        = true
    use_lockfile     = true
  }
}

provider "aws" {
  region = "us-east-1"
}

resource "aws_ecr_repository" "order_management_api" {
  name                 = "order-management-api"
  image_tag_mutability = "IMMUTABLE"

  tags = {
    Name        = "order-management-api"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

output "ecr_repository_url" {
  description = "Private ECR repository URL for GitHub Actions."
  value = aws_ecr_repository.order_management_api.repository_url
}