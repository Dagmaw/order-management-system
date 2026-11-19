resource "aws_route_table" "database" {
  vpc_id = aws_vpc.main.id

  tags = {
    Name        = "order-management-dev-database"
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_route_table_association" "database" {
  for_each = toset(["database_a", "database_b"])

  subnet_id      = aws_subnet.main[each.key].id
  route_table_id = aws_route_table.database.id
}