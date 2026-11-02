terraform {
  required_version = ">= 1.17.1, < 2.0.0"
}

variable "environment" {
    description = "Environment represented by this learning exercise."
    type = string
    default = "local"

    validation {
        condition = contains(
            ["local", "dev", "staging", "prod"],
            var.environment
        )
        error_message = "Environment must be local, dev, staging, or prod."
    }
}

variable "service_name" {
    description = "Name of the service."
    type = string
    default = "inventory-service"
}

variable "replicas" {
    description = "Example desired replica count stored for learning."
    type = number
    default = 2

    validation {
        condition = (
            var.replicas >= 1 &&
            var.replicas <= 3 &&
            floor(var.replicas) == var.replicas
        )
        error_message = "Replicas must be an integer between 1 and 3."
    }
}


resource "terraform_data" "inventory" {
    input = {
        environment  = var.environment
        service_name = var.service_name
        replicas     = var.replicas
    }
}

output "inventory_configuration" {
    description = "The configuration stored by the learning resource.   "
    value = terraform_data.inventory.output
}