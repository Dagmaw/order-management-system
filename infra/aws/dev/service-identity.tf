locals {
  service_identity_tags = {
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
  service_identity_domain = "oms-dev-services-638500336727"
  service_identity_issuer = "https://cognito-idp.us-east-1.amazonaws.com/${aws_cognito_user_pool.services.id}"
  service_token_endpoint  = "https://${aws_cognito_user_pool_domain.services.domain}.auth.us-east-1.amazoncognito.com/oauth2/token"
}

# Dedicated machine identities; existing Customer login remains unchanged.
resource "aws_cognito_user_pool" "services" {
  name                = "order-management-dev-services"
  user_pool_tier      = "LITE"
  deletion_protection = "ACTIVE"
  admin_create_user_config {
    allow_admin_create_user_only = true
  }
  tags = local.service_identity_tags
}

resource "aws_cognito_user_pool_domain" "services" {
  domain       = local.service_identity_domain
  user_pool_id = aws_cognito_user_pool.services.id
}

resource "aws_cognito_resource_server" "inventory" {
  identifier   = "inventory"
  name         = "Inventory Service"
  user_pool_id = aws_cognito_user_pool.services.id
  scope {
    scope_name        = "reserve"
    scope_description = "Reserve, inspect, and release inventory reservations"
  }
  scope {
    scope_name        = "stock.write"
    scope_description = "Administer stock; not granted to Orders"
  }
}

resource "aws_cognito_user_pool_client" "orders_service" {
  name                                 = "order-management-dev-orders-service"
  user_pool_id                         = aws_cognito_user_pool.services.id
  generate_secret                      = true
  allowed_oauth_flows_user_pool_client = true
  allowed_oauth_flows                  = ["client_credentials"]
  allowed_oauth_scopes                 = ["${aws_cognito_resource_server.inventory.identifier}/reserve"]
  explicit_auth_flows                  = ["ALLOW_REFRESH_TOKEN_AUTH"]
  prevent_user_existence_errors        = "ENABLED"
  access_token_validity                = 1
  token_validity_units {
    access_token = "hours"
  }
}

resource "aws_secretsmanager_secret" "orders_service_identity" {
  name                    = "order-management/dev/orders-service-identity"
  description             = "Cognito client credentials for Orders to Inventory"
  recovery_window_in_days = 7
  tags                    = local.service_identity_tags
}

resource "aws_secretsmanager_secret_version" "orders_service_identity" {
  secret_id = aws_secretsmanager_secret.orders_service_identity.id
  secret_string = jsonencode({
    clientId      = aws_cognito_user_pool_client.orders_service.id
    clientSecret  = aws_cognito_user_pool_client.orders_service.client_secret
    tokenEndpoint = local.service_token_endpoint
    issuer        = local.service_identity_issuer
    jwksUri       = "${local.service_identity_issuer}/.well-known/jwks.json"
    scope         = "inventory/reserve"
  })
}

output "cognito_service_identity" {
  description = "Non-secret service identity settings; client secret is only in Secrets Manager and protected Terraform state."
  value = {
    user_pool_id   = aws_cognito_user_pool.services.id
    client_id      = aws_cognito_user_pool_client.orders_service.id
    issuer         = local.service_identity_issuer
    jwks_uri       = "${local.service_identity_issuer}/.well-known/jwks.json"
    token_endpoint = local.service_token_endpoint
    scope          = "inventory/reserve"
    secret_arn     = aws_secretsmanager_secret.orders_service_identity.arn
  }
}

# This role can read only the dedicated M2M secret. The existing GitHub OIDC provider is reused.
resource "aws_iam_role" "service_identity_verification" {
  name = "order-management-dev-service-identity-test"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = "sts:AssumeRoleWithWebIdentity"
      Principal = {
        Federated = "arn:aws:iam::638500336727:oidc-provider/token.actions.githubusercontent.com"
      }
      Condition = {
        StringEquals = {
          "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com"
          "token.actions.githubusercontent.com:sub" = "repo:Dagmaw@31310623/order-management-system@1431786532:environment:terraform-dev"
        }
      }
    }]
  })
  tags = local.service_identity_tags
}

resource "aws_iam_role_policy" "service_identity_verification" {
  name = "read-orders-service-identity"
  role = aws_iam_role.service_identity_verification.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["secretsmanager:GetSecretValue"]
      Resource = aws_secretsmanager_secret.orders_service_identity.arn
    }]
  })
}
