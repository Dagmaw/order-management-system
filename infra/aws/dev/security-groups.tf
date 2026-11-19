resource "aws_security_group" "alb" {
  name        = "order-management-dev-alb"
  description = "Security group for the application load balancer"
  vpc_id      = aws_vpc.main.id

  tags = {
    Name        = "order-management-dev-alb"
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_security_group" "api" {
  name        = "order-management-dev-api"
  description = "Security group for the API tasks"
  vpc_id      = aws_vpc.main.id

  tags = {
    Name        = "order-management-dev-api"
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_security_group" "database" {
  name        = "order-management-dev-database"
  description = "Security group for PostgreSQL"
  vpc_id      = aws_vpc.main.id

  tags = {
    Name        = "order-management-dev-database"
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

# Clients can reach the load balancer over HTTPS.
resource "aws_vpc_security_group_ingress_rule" "alb_https" {
  security_group_id = aws_security_group.alb.id

  description = "HTTPS from clients"
  cidr_ipv4   = "0.0.0.0/0"
  ip_protocol = "tcp"
  from_port   = 443
  to_port     = 443
}

# The load balancer can send requests to the API.
resource "aws_vpc_security_group_egress_rule" "alb_to_api" {
  security_group_id            = aws_security_group.alb.id
  referenced_security_group_id = aws_security_group.api.id

  description = "Requests and health checks to API"
  ip_protocol = "tcp"
  from_port   = 8080
  to_port     = 8080
}

# The API accepts requests from the load balancer.
resource "aws_vpc_security_group_ingress_rule" "api_from_alb" {
  security_group_id            = aws_security_group.api.id
  referenced_security_group_id = aws_security_group.alb.id

  description = "API requests from load balancer"
  ip_protocol = "tcp"
  from_port   = 8080
  to_port     = 8080
}

# The API can connect to PostgreSQL.
resource "aws_vpc_security_group_egress_rule" "api_to_database" {
  security_group_id            = aws_security_group.api.id
  referenced_security_group_id = aws_security_group.database.id

  description = "PostgreSQL connections from API"
  ip_protocol = "tcp"
  from_port   = 5432
  to_port     = 5432
}

# PostgreSQL accepts connections from the API.
resource "aws_vpc_security_group_ingress_rule" "database_from_api" {
  security_group_id            = aws_security_group.database.id
  referenced_security_group_id = aws_security_group.api.id

  description = "PostgreSQL connections from API"
  ip_protocol = "tcp"
  from_port   = 5432
  to_port     = 5432
}

# API tasks can reach AWS service endpoints through the NAT gateway.
resource "aws_vpc_security_group_egress_rule" "api_https" {
  security_group_id = aws_security_group.api.id

  description = "HTTPS for image downloads, logs, and secrets"
  cidr_ipv4   = "0.0.0.0/0"
  ip_protocol = "tcp"
  from_port   = 443
  to_port     = 443
}