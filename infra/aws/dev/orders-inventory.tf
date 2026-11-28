# Release preparation only. Enable after the API candidate is published and reviewed.
variable "orders_inventory_enabled" {
  type    = bool
  default = false
}
locals {
  orders_inventory_environment = var.orders_inventory_enabled ? [
    { name = "Inventory__Enabled", value = "true" },
    { name = "Inventory__BaseUrl", value = "https://${local.inventory_hostname}/" },
    { name = "Inventory__TimeoutSeconds", value = "10" },
    { name = "Inventory__AuthenticationMode", value = "ClientCredentials" },
    { name = "Inventory__TokenEndpoint", value = local.service_token_endpoint },
    { name = "Inventory__ClientId", value = aws_cognito_user_pool_client.orders_service.id },
    { name = "Inventory__Scope", value = "inventory/reserve" }
  ] : []
  orders_inventory_secrets = var.orders_inventory_enabled ? [
    { name = "Inventory__ClientSecret", valueFrom = "${aws_secretsmanager_secret.orders_service_identity.arn}:clientSecret::" }
  ] : []
}
resource "aws_iam_role_policy" "api_inventory_identity" {
  count = var.orders_inventory_enabled ? 1 : 0
  name  = "api-inventory-identity"
  role  = aws_iam_role.api_execution.id
  policy = jsonencode({ Version = "2012-10-17", Statement = [{
    Effect   = "Allow", Action = "secretsmanager:GetSecretValue",
    Resource = aws_secretsmanager_secret.orders_service_identity.arn
  }] })
}
