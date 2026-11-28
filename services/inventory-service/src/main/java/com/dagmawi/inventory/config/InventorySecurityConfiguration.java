package com.dagmawi.inventory.config;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.security.oauth2.jwt.*;
import org.springframework.security.oauth2.core.*;
import org.springframework.security.oauth2.jose.jws.SignatureAlgorithm;
import org.springframework.http.HttpMethod;
import java.security.KeyFactory;
import java.security.interfaces.RSAPublicKey;
import java.security.spec.X509EncodedKeySpec;
import java.util.Base64;

@Configuration
public class InventorySecurityConfiguration {
    @Bean
    SecurityFilterChain inventorySecurity(HttpSecurity http) throws Exception {
        return http.csrf(csrf->csrf.disable())
                .sessionManagement(session->session.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
                .authorizeHttpRequests(auth->auth
                        .requestMatchers("/swagger-ui/**","/swagger-ui.html","/v3/api-docs/**").permitAll()
                        .requestMatchers(HttpMethod.GET,"/api/inventory/{productId}").permitAll()
                        .requestMatchers(HttpMethod.PUT,"/api/inventory/{productId}/stock").hasAuthority("SCOPE_inventory.stock.write")
                        .requestMatchers(HttpMethod.POST,"/api/inventory/reservations","/api/inventory/reservations/{id}/release").hasAuthority("SCOPE_inventory.reserve")
                        .requestMatchers(HttpMethod.GET,"/api/inventory/reservations/{id}").hasAuthority("SCOPE_inventory.reserve")
                        .anyRequest().denyAll())
                .oauth2ResourceServer(server->server.jwt(jwt->{}))
                .build();
    }

    @Bean
    JwtDecoder inventoryJwtDecoder(@Value("${inventory.jwt.public-key-base64}") String encodedKey,
                                  @Value("${inventory.jwt.issuer}") String issuer,
                                  @Value("${inventory.jwt.audience}") String audience) throws Exception {
        if(issuer.isBlank() || audience.isBlank())throw new IllegalArgumentException("JWT issuer and audience are required.");
        RSAPublicKey key;
        try {
            key=(RSAPublicKey)KeyFactory.getInstance("RSA").generatePublic(
                    new X509EncodedKeySpec(Base64.getDecoder().decode(encodedKey)));
        } catch(Exception e) {
            throw new IllegalArgumentException("Configure a valid Base64 DER Inventory RSA public key.");
        }
        if(key.getModulus().bitLength()<2048)throw new IllegalArgumentException("RSA key must be at least 2048 bits.");
        var decoder=NimbusJwtDecoder.withPublicKey(key).signatureAlgorithm(SignatureAlgorithm.RS256).build();
        OAuth2TokenValidator<Jwt> claims=jwt -> {
            boolean valid=jwt.getExpiresAt()!=null && jwt.getSubject()!=null && !jwt.getSubject().isBlank()
                    && jwt.getAudience().contains(audience);
            return valid?OAuth2TokenValidatorResult.success():OAuth2TokenValidatorResult.failure(
                    new OAuth2Error("invalid_token","Required Inventory claims are missing or invalid.",null));
        };
        decoder.setJwtValidator(new DelegatingOAuth2TokenValidator<>(JwtValidators.createDefaultWithIssuer(issuer),claims));
        return decoder;
    }
}
