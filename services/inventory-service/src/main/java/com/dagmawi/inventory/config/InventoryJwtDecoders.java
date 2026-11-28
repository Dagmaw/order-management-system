package com.dagmawi.inventory.config;

import java.net.URI;
import java.security.KeyFactory;
import java.security.interfaces.RSAPublicKey;
import java.security.spec.X509EncodedKeySpec;
import java.util.Base64;
import java.util.Set;
import org.springframework.security.oauth2.core.*;
import org.springframework.security.oauth2.jose.jws.SignatureAlgorithm;
import org.springframework.security.oauth2.jwt.*;
import org.springframework.web.client.RestOperations;

/** Explicit trust configuration: never derive a JWKS URL from an untrusted token. */
public final class InventoryJwtDecoders {
    private InventoryJwtDecoders() { }

    public static NimbusJwtDecoder local(String encodedKey, String issuer, String audience) {
        RSAPublicKey key;
        try {
            key = (RSAPublicKey) KeyFactory.getInstance("RSA").generatePublic(
                    new X509EncodedKeySpec(Base64.getDecoder().decode(encodedKey)));
        } catch (Exception e) {
            throw new IllegalArgumentException("Configure a valid Base64 DER Inventory RSA public key.");
        }
        if (key.getModulus().bitLength() < 2048) throw new IllegalArgumentException("RSA key must be at least 2048 bits.");
        var decoder = NimbusJwtDecoder.withPublicKey(key).signatureAlgorithm(SignatureAlgorithm.RS256).build();
        decoder.setJwtValidator(validators(issuer, audience, Set.of(), false));
        return decoder;
    }

    public static NimbusJwtDecoder provider(String issuer, String audience, String jwks,
                                            Set<String> clientIds, RestOperations rest) {
        return provider(issuer, audience, jwks, clientIds, rest, false);
    }

    public static NimbusJwtDecoder cognito(String issuer, String jwks, Set<String> clients, RestOperations rest) {
        if (!issuer.matches("https://cognito-idp\\.[a-z0-9-]+\\.amazonaws\\.com/[a-z0-9-]+_[A-Za-z0-9]+")
                || !jwks.equals(issuer + "/.well-known/jwks.json"))
            throw new IllegalArgumentException("Cognito requires the exact pool issuer and its JWKS endpoint.");
        return provider(issuer, "", jwks, clients, rest, true);
    }

    private static NimbusJwtDecoder provider(String issuer, String audience, String jwks, Set<String> clientIds,
                                              RestOperations rest, boolean cognito) {
        requireHttps(issuer);
        requireHttps(jwks);
        if (clientIds.isEmpty() || clientIds.stream().anyMatch(String::isBlank))
            throw new IllegalArgumentException("Configure allowed Inventory service client IDs.");
        var decoder = NimbusJwtDecoder.withJwkSetUri(jwks).jwsAlgorithm(SignatureAlgorithm.RS256).validateType(false)
                .restOperations(rest).build();
        decoder.setJwtValidator(validators(issuer, audience, Set.copyOf(clientIds), cognito));
        return decoder;
    }

    private static OAuth2TokenValidator<Jwt> validators(String issuer, String audience, Set<String> clients, boolean cognito) {
        if (issuer.isBlank() || (!cognito && audience.isBlank())) throw new IllegalArgumentException("JWT issuer and audience are required.");
        OAuth2TokenValidator<Jwt> claims = jwt -> {
            boolean valid = jwt.getExpiresAt() != null && jwt.getSubject() != null && !jwt.getSubject().isBlank()
                    && (cognito || (jwt.getAudience() != null && jwt.getAudience().contains(audience)));
            if (!clients.isEmpty()) {
                Object client = jwt.getClaims().get("client_id");
                Object use = jwt.getClaims().get("token_use");
                Object type = jwt.getHeaders().get("typ");
                valid = valid && client instanceof String && clients.contains(client)
                        && (cognito ? "access".equals(use) && jwt.getSubject().equals(client) : use == null || "access".equals(use))
                        && ((cognito && type == null) || "JWT".equals(type) || "at+jwt".equals(type));
            }
            return valid ? OAuth2TokenValidatorResult.success() : OAuth2TokenValidatorResult.failure(
                    new OAuth2Error("invalid_token", "Required Inventory claims are missing or invalid.", null));
        };
        if (clients.isEmpty())
            return new DelegatingOAuth2TokenValidator<>(JwtValidators.createDefaultWithIssuer(issuer), claims);
        // Provider JWT and at+jwt types are checked explicitly above.
        return new DelegatingOAuth2TokenValidator<>(new JwtTimestampValidator(), new JwtIssuerValidator(issuer), claims);
    }

    public static void requireHttps(String endpoint) {
        try {
            var uri = URI.create(endpoint);
            if (!"https".equals(uri.getScheme()) || uri.getHost() == null || uri.getUserInfo() != null
                    || uri.getQuery() != null || uri.getFragment() != null) throw new IllegalArgumentException();
        } catch (IllegalArgumentException e) {
            throw new IllegalArgumentException("Inventory issuer and JWKS endpoints must be explicit HTTPS URLs without credentials, query, or fragment.");
        }
    }
}
