package com.dagmawi.inventory.config;

import com.nimbusds.jose.*;
import com.nimbusds.jose.crypto.RSASSASigner;
import com.nimbusds.jwt.*;
import java.net.URI;
import java.net.http.*;
import java.security.KeyPair;
import java.time.*;
import java.util.*;
import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.boot.test.web.server.LocalServerPort;
import org.springframework.context.annotation.*;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.client.MockRestServiceServer;
import org.springframework.web.client.*;
import org.springframework.http.MediaType;
import static org.junit.jupiter.api.Assertions.*;
import static org.springframework.test.web.client.match.MockRestRequestMatchers.requestTo;
import static org.springframework.test.web.client.response.MockRestResponseCreators.withSuccess;

@ActiveProfiles("test")
@SpringBootTest(webEnvironment=SpringBootTest.WebEnvironment.RANDOM_PORT,properties={
    "inventory.operations.enabled=true","inventory.jwt.mode=Jwks",
    "inventory.jwt.issuer=https://identity.example/realm","inventory.jwt.audience=Inventory.API",
    "inventory.jwt.jwk-set-uri=https://identity.example/keys","inventory.jwt.allowed-client-ids=orders,stock-admin",
    "inventory.jwt.reservation-scope=inventory/reserve","inventory.jwt.stock-scope=inventory/stock.write"})
@Import(InventoryProviderHttpTests.KeysConfiguration.class)
class InventoryProviderHttpTests {
    static final KeyPair KEY=key();
    static KeyPair key() {try{return InventoryProviderJwtTests.key();}catch(Exception e){throw new IllegalStateException(e);}}
    @TestConfiguration static class KeysConfiguration {
        @Bean @Primary RestOperations providerTestRestOperations() {
            var rest=new RestTemplate();var server=MockRestServiceServer.bindTo(rest).build();
            server.expect(org.springframework.test.web.client.ExpectedCount.manyTimes(),requestTo("https://identity.example/keys"))
                .andRespond(withSuccess(InventoryProviderJwtTests.jwks(KEY,"provider"),MediaType.APPLICATION_JSON));
            return rest;
        }
    }
    @LocalServerPort int port;
    String token(String client,String scope,String use) throws Exception {
        var claims=new JWTClaimsSet.Builder().issuer("https://identity.example/realm").audience("Inventory.API")
            .subject("service-account").claim("client_id",client).claim("scope",scope)
            .claim("token_use",use).expirationTime(Date.from(Instant.now().plusSeconds(300))).build();
        var jwt=new SignedJWT(new JWSHeader.Builder(JWSAlgorithm.RS256).keyID("provider").type(JOSEObjectType.JWT).build(),claims);
        jwt.sign(new RSASSASigner(KEY.getPrivate()));return jwt.serialize();
    }
    int call(String method,String path,String token,String body) throws Exception {
        var builder=HttpRequest.newBuilder(URI.create("http://localhost:"+port+path)).timeout(Duration.ofSeconds(10)).header("Content-Type","application/json");
        if(token!=null)builder.header("Authorization","Bearer "+token);
        return HttpClient.newHttpClient().send(builder.method(method,body==null?HttpRequest.BodyPublishers.noBody():HttpRequest.BodyPublishers.ofString(body)).build(),HttpResponse.BodyHandlers.discarding()).statusCode();
    }
    @Test void identityAndPermissionFailuresRemain401And403() throws Exception {
        assertEquals(401,call("POST","/api/inventory/reservations",null,"{}"));
        assertEquals(401,call("POST","/api/inventory/reservations",token("other","inventory/reserve","access"),"{}"));
        assertEquals(401,call("POST","/api/inventory/reservations",token("orders","inventory/reserve","id"),"{}"));
        assertEquals(403,call("POST","/api/inventory/reservations",token("orders","","access"),"{}"));
        assertEquals(403,call("POST","/api/inventory/reservations",token("stock-admin","inventory/stock.write","access"),"{}"));
        assertEquals(400,call("POST","/api/inventory/reservations",token("orders","inventory/reserve","access"),"{}"));
    }
    @Test void providerTokenReservesAndReleasesWithoutStockAdministrationPermission() throws Exception {
        var id=UUID.randomUUID();var token=token("orders","inventory/reserve","access");
        var body="{\"reservationId\":\""+id+"\",\"items\":[{\"productId\":1,\"quantity\":1}]}";
        assertEquals(200,call("POST","/api/inventory/reservations",token,body));
        assertEquals(200,call("GET","/api/inventory/reservations/"+id,token,null));
        assertEquals(200,call("POST","/api/inventory/reservations/"+id+"/release",token,null));
        assertEquals(403,call("PUT","/api/inventory/1/stock",token,"{}"));
    }
}
