resource "aws_eip" "nat" {
  domain = "vpc"

  tags = {
    Name        = "order-management-dev-nat-eip"
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_nat_gateway" "main" {
  allocation_id = aws_eip.nat.id
  subnet_id     = aws_subnet.main["public_a"].id

  depends_on = [
    aws_internet_gateway.main,
    aws_route.public_internet,
    aws_route_table_association.public
  ]

  tags = {
    Name        = "order-management-dev-nat"
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_route_table" "application" {
  vpc_id = aws_vpc.main.id

  tags = {
    Name        = "order-management-dev-application"
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_route" "application_outbound" {
  route_table_id         = aws_route_table.application.id
  destination_cidr_block = "0.0.0.0/0"
  nat_gateway_id         = aws_nat_gateway.main.id
}

resource "aws_route_table_association" "application" {
  for_each = toset(["app_a", "app_b"])

  subnet_id      = aws_subnet.main[each.key].id
  route_table_id = aws_route_table.application.id
}

output "nat_gateway_id" {
  description = "NAT gateway providing outbound application access."
  value       = aws_nat_gateway.main.id
}