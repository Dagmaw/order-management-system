resource "aws_db_subnet_group" "main" {
  name        = "order-management-dev-database"
  description = "Private database subnets for order management"

  subnet_ids = [
    aws_subnet.main["database_a"].id,
    aws_subnet.main["database_b"].id
  ]

  tags = {
    Name        = "order-management-dev-database"
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_db_instance" "main" {
  identifier = "order-management-dev-postgres"

  engine                      = "postgres"
  engine_version              = "18"
  instance_class              = "db.t4g.micro"
  auto_minor_version_upgrade  = true
  allow_major_version_upgrade = false

  db_name                     = "ordermanagement"
  username                    = "dbadmin"
  manage_master_user_password = true
  port                        = 5432

  allocated_storage = 20
  storage_type      = "gp3"
  storage_encrypted = true

  db_subnet_group_name   = aws_db_subnet_group.main.name
  vpc_security_group_ids = [aws_security_group.database.id]
  publicly_accessible    = false
  multi_az               = false

  backup_retention_period = 7
  backup_window           = "03:00-04:00"
  maintenance_window      = "sun:05:00-sun:06:00"
  copy_tags_to_snapshot   = true

  deletion_protection      = false
  skip_final_snapshot      = true
  delete_automated_backups = true

  tags = {
    Name        = "order-management-dev-postgres"
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}