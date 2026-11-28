package com.dagmawi.inventory.config;

import io.swagger.v3.oas.models.OpenAPI;
import io.swagger.v3.oas.models.info.Info;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

@Configuration
public class OpenApiConfiguration {
    @Bean
    public OpenAPI inventoryOpenApi() {
        return new OpenAPI().info(new Info()
                .title("Inventory Service API")
                .version("v1")
                .description("PostgreSQL-backed inventory lookup. Reservation and stock operations are enabled only by explicit local configuration. "
                        + "This service does not yet enforce JWT authentication."));
    }
}
