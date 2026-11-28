output "deployment_verification" {
  description = "Non-secret expectations for read-only post-deployment checks."
  value = {
    account_id      = "638500336727"
    region          = "us-east-1"
    cluster         = aws_ecs_cluster.main.name
    public_base_url = "https://${local.api_hostname}"
    services = concat([
      {
        name                = aws_ecs_service.api.name
        task_definition_arn = aws_ecs_task_definition.api.arn
        container           = "api"
        image               = local.api_image
        target_group_arn    = aws_lb_target_group.api.arn
      }
      ], var.inventory_image != "" ? [
      {
        name                = aws_ecs_service.inventory[0].name
        task_definition_arn = aws_ecs_task_definition.inventory[0].arn
        container           = "inventory"
        image               = var.inventory_image
        target_group_arn    = aws_lb_target_group.inventory.arn
      }
    ] : [])
  }
}
