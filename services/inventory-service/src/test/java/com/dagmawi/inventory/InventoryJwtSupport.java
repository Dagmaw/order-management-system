package com.dagmawi.inventory;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import com.nimbusds.jose.*;
import com.nimbusds.jose.crypto.RSASSASigner;
import com.nimbusds.jwt.*;
import java.security.*;
import java.util.*;
import java.time.Instant;

abstract class InventoryJwtSupport {
    static final KeyPair KEYS=keys();
    static KeyPair keys(){try {var generator=KeyPairGenerator.getInstance("RSA");generator.initialize(2048);return generator.generateKeyPair();}
        catch(Exception e){throw new IllegalStateException(e);}}
    @DynamicPropertySource static void jwtProperties(DynamicPropertyRegistry properties) {
        properties.add("inventory.jwt.public-key-base64",()->Base64.getEncoder().encodeToString(KEYS.getPublic().getEncoded()));
        properties.add("inventory.jwt.issuer",()->"Inventory.Tests");
        properties.add("inventory.jwt.audience",()->"Inventory.API");
    }
    static String token(String scope){return sign(scope,"Inventory.Tests","Inventory.API",Instant.now().plusSeconds(300),KEYS,true);}
    static String sign(String scope,String issuer,String audience,Instant expiry,KeyPair keys,boolean includeExpiry) {
        try {
            var claims=new JWTClaimsSet.Builder().issuer(issuer).audience(audience).subject("orders-test")
                    .issueTime(Date.from(Instant.now())).claim("scope",scope);
            if(includeExpiry)claims.expirationTime(Date.from(expiry));
            var jwt=new SignedJWT(new JWSHeader.Builder(JWSAlgorithm.RS256).type(JOSEObjectType.JWT).build(),claims.build());
            jwt.sign(new RSASSASigner(keys.getPrivate()));return jwt.serialize();
        }catch(Exception e){throw new IllegalStateException(e);}
    }
}
