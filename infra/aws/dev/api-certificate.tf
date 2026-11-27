locals {
  api_hostname = "api-dev.kaddigitalsolutions.com"
}

data "aws_route53_zone" "company" {
  zone_id = "Z09662321NARFICJD1XGZ"
}

resource "aws_acm_certificate" "api" {
  domain_name       = local.api_hostname
  validation_method = "DNS"
  key_algorithm     = "RSA_2048"

  lifecycle {
    create_before_destroy = true
  }

  tags = {
    Name        = "order-management-dev-api"
    Project     = "order-management-system"
    Environment = "dev"
    ManagedBy   = "Terraform"
  }
}

resource "aws_route53_record" "api_certificate_validation" {
  for_each = {
    for option in aws_acm_certificate.api.domain_validation_options :
    option.domain_name => {
      name   = option.resource_record_name
      type   = option.resource_record_type
      record = option.resource_record_value
    }
  }

  zone_id = data.aws_route53_zone.company.zone_id
  name    = each.value.name
  type    = each.value.type
  ttl     = 60
  records = [each.value.record]
}

resource "aws_acm_certificate_validation" "api" {
  certificate_arn = aws_acm_certificate.api.arn
  validation_record_fqdns = [
    for record in aws_route53_record.api_certificate_validation : record.fqdn
  ]

  timeouts {
    create = "45m"
  }
}

output "api_hostname" {
  description = "Development API hostname; traffic record is created with the load balancer."
  value       = local.api_hostname
}

output "api_certificate_arn" {
  description = "Validated ACM certificate for the development API."
  value       = aws_acm_certificate_validation.api.certificate_arn
}
