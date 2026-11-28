package com.dagmawi.inventory.config;

import io.swagger.v3.oas.models.OpenAPI;
import io.swagger.v3.oas.models.info.Info;
import org.springframework.context.annotation.Bean;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Configuration;

@Configuration
public class OpenApiConfiguration {
    @Bean
    public OpenAPI inventoryOpenApi(
            @Value("${inventory.jwt.reservation-scope:inventory.reserve}") String reserveScope,
            @Value("${inventory.jwt.stock-scope:inventory.stock.write}") String stockScope) {
        return new OpenAPI().components(new io.swagger.v3.oas.models.Components()
                .addSecuritySchemes("InventoryBearer",new io.swagger.v3.oas.models.security.SecurityScheme()
                        .type(io.swagger.v3.oas.models.security.SecurityScheme.Type.HTTP).scheme("bearer").bearerFormat("JWT")
                        .description("Dedicated Inventory RS256 access token. Stock requires " + stockScope + "; reservations require " + reserveScope + ".")))
                .info(new Info()
                .title("Inventory Service API")
                .version("v1")
                .description("PostgreSQL-backed inventory lookup. Reservation and stock operations are enabled only by explicit configuration. "
                        + "JWT scopes protect stock and reservation operations. Lookup remains public."));
    }
}
