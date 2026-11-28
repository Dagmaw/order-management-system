package com.dagmawi.inventory.config;
import com.nimbusds.jose.*;
import com.nimbusds.jose.crypto.RSASSASigner;
import com.nimbusds.jwt.*;
import java.security.KeyPair;
import java.time.Instant;
import java.util.*;
import org.junit.jupiter.api.Test;
import org.springframework.http.MediaType;
import org.springframework.security.oauth2.jwt.*;
import org.springframework.test.web.client.MockRestServiceServer;
import org.springframework.web.client.RestTemplate;
import static org.junit.jupiter.api.Assertions.*;
import static org.springframework.test.web.client.match.MockRestRequestMatchers.requestTo;
import static org.springframework.test.web.client.response.MockRestResponseCreators.withSuccess;
class CognitoInventoryJwtTests {
    static final String ISSUER="https://cognito-idp.us-east-1.amazonaws.com/us-east-1_TestPool";
    static final String JWKS=ISSUER+"/.well-known/jwks.json";
    static String token(KeyPair key,String issuer,String subject,String client,String use,String audience) throws Exception {
        var claims=new JWTClaimsSet.Builder().issuer(issuer).subject(subject).claim("client_id",client)
            .claim("token_use",use).claim("scope","inventory/reserve").expirationTime(Date.from(Instant.now().plusSeconds(300)));
        if(audience!=null)claims.audience(audience);
        var jwt=new SignedJWT(new JWSHeader.Builder(JWSAlgorithm.RS256).keyID("cognito").build(),claims.build());
        jwt.sign(new RSASSASigner(key.getPrivate()));return jwt.serialize();
    }
    @Test void cognitoM2mWithoutAudienceRequiresAccessTokenAndMatchingMachineSubject() throws Exception {
        var key=InventoryProviderJwtTests.key();var rest=new RestTemplate();var server=MockRestServiceServer.bindTo(rest).build();
        server.expect(requestTo(JWKS)).andRespond(withSuccess(InventoryProviderJwtTests.jwks(key,"cognito"),MediaType.APPLICATION_JSON));
        var decoder=InventoryJwtDecoders.cognito(ISSUER,JWKS,Set.of("orders"),rest);
        assertEquals("orders",decoder.decode(token(key,ISSUER,"orders","orders","access",null)).getSubject());
        for(var invalid:List.of(token(key,ISSUER,"user-subject","orders","access",null),
            token(key,ISSUER,"other","other","access",null),token(key,ISSUER,"orders","orders","id",null),
            token(key,ISSUER,"orders","orders",null,null),token(key,"https://wrong.example","orders","orders","access",null)))
            assertThrows(JwtException.class,()->decoder.decode(invalid));
        server.verify();
    }
    @Test void genericModeStillRequiresApiAudience() throws Exception {
        var key=InventoryProviderJwtTests.key();var rest=new RestTemplate();var server=MockRestServiceServer.bindTo(rest).build();
        server.expect(requestTo(JWKS)).andRespond(withSuccess(InventoryProviderJwtTests.jwks(key,"cognito"),MediaType.APPLICATION_JSON));
        var decoder=InventoryJwtDecoders.provider(ISSUER,"Inventory.API",JWKS,Set.of("orders"),rest);
        var noAudience=token(key,ISSUER,"orders","orders","access",null);
        assertThrows(JwtException.class,()->decoder.decode(noAudience));server.verify();
    }
    @Test void cognitoTrustCannotPointAtAnotherIssuerOrJwksLocation() {
        assertThrows(IllegalArgumentException.class,()->InventoryJwtDecoders.cognito("https://other.example",JWKS,Set.of("orders"),new RestTemplate()));
        assertThrows(IllegalArgumentException.class,()->InventoryJwtDecoders.cognito(ISSUER,"https://other.example/keys",Set.of("orders"),new RestTemplate()));
    }
    @Test void genericModeStillRejectsMissingTypeEvenWhenAudienceMatches() throws Exception {
        var key=InventoryProviderJwtTests.key();var rest=new RestTemplate();var server=MockRestServiceServer.bindTo(rest).build();
        server.expect(requestTo(JWKS)).andRespond(withSuccess(InventoryProviderJwtTests.jwks(key,"cognito"),MediaType.APPLICATION_JSON));
        var decoder=InventoryJwtDecoders.provider(ISSUER,"Inventory.API",JWKS,Set.of("orders"),rest);
        var missingType=token(key,ISSUER,"orders","orders","access","Inventory.API");
        assertThrows(JwtException.class,()->decoder.decode(missingType));server.verify();
    }
}
