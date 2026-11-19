data "aws_availability_zones" "available" {
  state = "available"

  filter {
    name   = "opt-in-status"
    values = ["opt-in-not-required"]
  }
}

locals {
  subnets = {
    public_a = {
      cidr = "10.20.0.0/24"
      zone = 0
      tier = "public"
    }
    public_b = {
      cidr = "10.20.1.0/24"
      zone = 1
      tier = "public"
    }
    app_a = {
      cidr = "10.20.10.0/24"
      zone = 0
      tier = "application"
    }
    app_b = {
      cidr = "10.20.11.0/24"
      zone = 1
      tier = "application"
    }
    database_a = {
      cidr = "10.20.20.0/24"
      zone = 0
      tier = "database"
    }
    database_b = {
      cidr = "10.20.21.0/24"
      zone = 1
      tier = "database"
    }
  }
}

resource "aws_subnet" "main" {
  for_each = local.subnets

  vpc_id                  = aws_vpc.main.id
  cidr_block              = each.value.cidr
  availability_zone       = data.aws_availability_zones.available.names[each.value.zone]
  map_public_ip_on_launch = false

  tags = {
    Name        = "order-management-dev-${each.key}"
    Project     = "order-management-system"
    Environment = "dev"
    Tier        = each.value.tier
    ManagedBy   = "Terraform"
  }
}

output "subnet_ids" {
  description = "Development subnet IDs, indexed by subnet name."
  value = {
    for name, subnet in aws_subnet.main : name => subnet.id
  }
}