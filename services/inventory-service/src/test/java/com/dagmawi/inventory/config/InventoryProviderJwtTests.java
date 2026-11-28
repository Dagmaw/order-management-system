package com.dagmawi.inventory.config;
import com.nimbusds.jose.*;
import com.nimbusds.jose.crypto.RSASSASigner;
import com.nimbusds.jose.jwk.*;
import com.nimbusds.jwt.*;
import java.security.*;
import java.security.interfaces.RSAPublicKey;
import java.time.Instant;
import java.util.*;
import org.junit.jupiter.api.Test;
import org.springframework.mock.env.MockEnvironment;
import org.springframework.security.oauth2.jwt.*;
import org.springframework.test.web.client.MockRestServiceServer;
import org.springframework.web.client.RestTemplate;
import org.springframework.http.MediaType;
import static org.junit.jupiter.api.Assertions.*;
import static org.springframework.test.web.client.match.MockRestRequestMatchers.requestTo;
import static org.springframework.test.web.client.response.MockRestResponseCreators.*;
class InventoryProviderJwtTests {
    static final String ISSUER="https://identity.example/realm", JWKS="https://identity.example/keys";
    static KeyPair key() throws Exception { var g=KeyPairGenerator.getInstance("RSA");g.initialize(2048);return g.generateKeyPair(); }
    static String jwks(KeyPair key,String kid) {return new JWKSet(new RSAKey.Builder((RSAPublicKey)key.getPublic()).keyID(kid).build()).toString();}
    static String token(KeyPair key,String kid,String issuer,String audience,String client,Instant expiry,Instant nbf,JWSAlgorithm algorithm) throws Exception {
        var claims=new JWTClaimsSet.Builder().issuer(issuer).audience(audience).subject("service-account")
                .claim("client_id",client).claim("scope","inventory.reserve").notBeforeTime(Date.from(nbf));
        if(expiry!=null)claims.expirationTime(Date.from(expiry));
        var jwt=new SignedJWT(new JWSHeader.Builder(algorithm).keyID(kid).type(JOSEObjectType.JWT).build(),claims.build());
        jwt.sign(new RSASSASigner(key.getPrivate()));return jwt.serialize();
    }
    static String token(KeyPair key,String kid) throws Exception {return token(key,kid,ISSUER,"Inventory.API","orders",Instant.now().plusSeconds(300),Instant.now().minusSeconds(1),JWSAlgorithm.RS256);}
    @Test void trustedKeyAndClientAreAcceptedAndNewKeyIdTriggersRotation() throws Exception {
        var first=key();var rotated=key();var rest=new RestTemplate();var server=MockRestServiceServer.bindTo(rest).build();
        server.expect(requestTo(JWKS)).andRespond(withSuccess(jwks(first,"first"),MediaType.APPLICATION_JSON));
        server.expect(requestTo(JWKS)).andRespond(withSuccess(jwks(rotated,"rotated"),MediaType.APPLICATION_JSON));
        var decoder=InventoryJwtDecoders.provider(ISSUER,"Inventory.API",JWKS,Set.of("orders"),rest);
        assertEquals("orders",decoder.decode(token(first,"first")).getClaimAsString("client_id"));
        assertEquals("orders",decoder.decode(token(rotated,"rotated")).getClaimAsString("client_id"));server.verify();
    }
    @Test void invalidClaimsAndAlgorithmsFailClosed() throws Exception {
        var key=key();var stranger=key();var rest=new RestTemplate();var server=MockRestServiceServer.bindTo(rest).build();
        server.expect(org.springframework.test.web.client.ExpectedCount.manyTimes(),requestTo(JWKS)).andRespond(withSuccess(jwks(key,"known"),MediaType.APPLICATION_JSON));
        var decoder=InventoryJwtDecoders.provider(ISSUER,"Inventory.API",JWKS,Set.of("orders"),rest);
        for(var invalid:List.of(
            token(key,"known","https://wrong.example","Inventory.API","orders",Instant.now().plusSeconds(300),Instant.now(),JWSAlgorithm.RS256),
            token(key,"known",ISSUER,"wrong","orders",Instant.now().plusSeconds(300),Instant.now(),JWSAlgorithm.RS256),
            token(key,"known",ISSUER,"Inventory.API","other-client",Instant.now().plusSeconds(300),Instant.now(),JWSAlgorithm.RS256),
            token(key,"known",ISSUER,"Inventory.API",null,Instant.now().plusSeconds(300),Instant.now(),JWSAlgorithm.RS256),
            token(key,"known",ISSUER,"Inventory.API","orders",Instant.now().minusSeconds(300),Instant.now().minusSeconds(600),JWSAlgorithm.RS256),
            token(key,"known",ISSUER,"Inventory.API","orders",null,Instant.now(),JWSAlgorithm.RS256),
            token(key,"known",ISSUER,"Inventory.API","orders",Instant.now().plusSeconds(600),Instant.now().plusSeconds(300),JWSAlgorithm.RS256),
            token(stranger,"known"),token(key,"unknown"),
            token(key,"known",ISSUER,"Inventory.API","orders",Instant.now().plusSeconds(300),Instant.now(),JWSAlgorithm.RS512),"not-a-jwt")) {
            assertThrows(JwtException.class,()->decoder.decode(invalid));
        }server.verify();
    }
    @Test void jwksOutageDoesNotAuthenticateAnUnverifiableToken() throws Exception {
        var rest=new RestTemplate();var server=MockRestServiceServer.bindTo(rest).build();
        server.expect(org.springframework.test.web.client.ExpectedCount.manyTimes(),requestTo(JWKS)).andRespond(withServerError());
        var decoder=InventoryJwtDecoders.provider(ISSUER,"Inventory.API",JWKS,Set.of("orders"),rest);
        var token=token(key(),"unavailable");assertThrows(JwtException.class,()->decoder.decode(token));server.verify();
    }
    @Test void unsafeTrustSettingsAndEmptyAllowlistAreRejectedBeforeAnyNetworkCall() {
        for(var unsafe:List.of("http://identity.example/keys","https://user:password@identity.example/keys","https://identity.example/keys?x=1","https://identity.example/keys#fragment"))
            assertThrows(IllegalArgumentException.class,()->InventoryJwtDecoders.provider(ISSUER,"Inventory.API",unsafe,Set.of("orders"),new RestTemplate()));
        assertThrows(IllegalArgumentException.class,()->InventoryJwtDecoders.provider(ISSUER,"Inventory.API",JWKS,Set.of(),new RestTemplate()));
    }
    @Test void productionRejectsLocalKeyModeAndUnknownModes() {
        var env=new MockEnvironment();env.setActiveProfiles("production");var config=new InventorySecurityConfiguration();
        assertThrows(IllegalArgumentException.class,()->config.inventoryJwtDecoder(env,"LocalRsa","",ISSUER,"Inventory.API","","",new RestTemplate()));
        assertThrows(IllegalArgumentException.class,()->config.inventoryJwtDecoder(env,"unknown","",ISSUER,"Inventory.API","","",new RestTemplate()));
    }
    @Test void accessTokenMediaTypeIsSupportedAndOtherTypesAreRejected() throws Exception {
        var key=key();var rest=new RestTemplate();var server=MockRestServiceServer.bindTo(rest).build();
        server.expect(requestTo(JWKS)).andRespond(withSuccess(jwks(key,"known"),MediaType.APPLICATION_JSON));
        var decoder=InventoryJwtDecoders.provider(ISSUER,"Inventory.API",JWKS,Set.of("orders"),rest);
        var claims=SignedJWT.parse(token(key,"known")).getJWTClaimsSet();
        var access=new SignedJWT(new JWSHeader.Builder(JWSAlgorithm.RS256).keyID("known").type(new JOSEObjectType("at+jwt")).build(),claims);
        access.sign(new RSASSASigner(key.getPrivate()));assertEquals("orders",decoder.decode(access.serialize()).getClaimAsString("client_id"));
        var other=new SignedJWT(new JWSHeader.Builder(JWSAlgorithm.RS256).keyID("known").type(new JOSEObjectType("unexpected")).build(),claims);
        other.sign(new RSASSASigner(key.getPrivate()));assertThrows(JwtException.class,()->decoder.decode(other.serialize()));server.verify();
    }
}
