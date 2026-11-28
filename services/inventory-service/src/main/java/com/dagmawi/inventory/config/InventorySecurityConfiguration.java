package com.dagmawi.inventory.config;

import java.io.IOException;
import java.net.HttpURLConnection;
import java.util.Arrays;
import java.util.stream.Collectors;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.core.env.Environment;
import org.springframework.http.HttpMethod;
import org.springframework.http.client.SimpleClientHttpRequestFactory;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.oauth2.jwt.JwtDecoder;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.web.client.RestTemplate;

@Configuration
public class InventorySecurityConfiguration {
    @Bean
    SecurityFilterChain inventorySecurity(HttpSecurity http,
            @Value("${inventory.jwt.reservation-scope:inventory.reserve}") String reserveScope,
            @Value("${inventory.jwt.stock-scope:inventory.stock.write}") String stockScope) throws Exception {
        if (reserveScope.isBlank() || stockScope.isBlank() || reserveScope.chars().anyMatch(Character::isWhitespace)
                || stockScope.chars().anyMatch(Character::isWhitespace) || reserveScope.equals(stockScope))
            throw new IllegalArgumentException("Configure distinct, nonempty Inventory permission scopes.");
        return http.csrf(csrf -> csrf.disable())
                .sessionManagement(session -> session.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
                .authorizeHttpRequests(auth -> auth
                        .requestMatchers("/swagger-ui/**", "/swagger-ui.html", "/v3/api-docs/**").permitAll()
                        .requestMatchers(HttpMethod.GET, "/api/inventory/{productId}").permitAll()
                        .requestMatchers(HttpMethod.PUT, "/api/inventory/{productId}/stock").hasAuthority("SCOPE_" + stockScope)
                        .requestMatchers(HttpMethod.POST, "/api/inventory/reservations", "/api/inventory/reservations/{id}/release").hasAuthority("SCOPE_" + reserveScope)
                        .requestMatchers(HttpMethod.GET, "/api/inventory/reservations/{id}").hasAuthority("SCOPE_" + reserveScope)
                        .anyRequest().denyAll())
                .oauth2ResourceServer(server -> server.jwt(jwt -> { }))
                .build();
    }

    @Bean
    JwtDecoder inventoryJwtDecoder(Environment environment,
            @Value("${inventory.jwt.mode:LocalRsa}") String mode,
            @Value("${inventory.jwt.public-key-base64:}") String encodedKey,
            @Value("${inventory.jwt.issuer}") String issuer,
            @Value("${inventory.jwt.audience}") String audience,
            @Value("${inventory.jwt.jwk-set-uri:}") String jwks,
            @Value("${inventory.jwt.allowed-client-ids:}") String clientIds,
            org.springframework.web.client.RestOperations inventoryJwksRestOperations) {
        if ("LocalRsa".equals(mode)) {
            if (environment.matchesProfiles("production", "prod"))
                throw new IllegalArgumentException("Local Inventory signing keys are not allowed in production.");
            return InventoryJwtDecoders.local(encodedKey, issuer, audience);
        }
        if (!"Jwks".equals(mode)) throw new IllegalArgumentException("Inventory JWT mode must be LocalRsa or Jwks.");
        var clients = Arrays.stream(clientIds.split(",")).map(String::trim)
                .filter(value -> !value.isEmpty()).collect(Collectors.toSet());
        return InventoryJwtDecoders.provider(issuer, audience, jwks, clients, inventoryJwksRestOperations);
    }

    @Bean
    org.springframework.web.client.RestOperations inventoryJwksRestOperations() {
        var factory = new SimpleClientHttpRequestFactory() {
            @Override protected void prepareConnection(HttpURLConnection connection, String method) throws IOException {
                super.prepareConnection(connection, method);
                connection.setInstanceFollowRedirects(false);
            }
        };
        factory.setConnectTimeout(3000);
        factory.setReadTimeout(3000);
        return new RestTemplate(factory);
    }
}
