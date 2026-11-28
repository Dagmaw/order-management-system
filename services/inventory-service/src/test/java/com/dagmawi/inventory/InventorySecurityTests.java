package com.dagmawi.inventory;
import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.web.server.LocalServerPort;
import org.springframework.test.context.ActiveProfiles;
import java.net.URI;
import java.net.http.*;
import java.time.*;
import java.util.UUID;
import static org.junit.jupiter.api.Assertions.*;

@ActiveProfiles("test")
@SpringBootTest(webEnvironment=SpringBootTest.WebEnvironment.RANDOM_PORT,properties="inventory.operations.enabled=true")
class InventorySecurityTests extends InventoryJwtSupport {
    @LocalServerPort int port;
    HttpResponse<String> call(String method,String path,String token,String body) throws Exception {
        var builder=HttpRequest.newBuilder(URI.create("http://localhost:"+port+path)).timeout(Duration.ofSeconds(10))
                .header("Content-Type","application/json");
        if(token!=null)builder.header("Authorization","Bearer "+token);
        return HttpClient.newHttpClient().send(builder.method(method,body==null?HttpRequest.BodyPublishers.noBody():HttpRequest.BodyPublishers.ofString(body)).build(),HttpResponse.BodyHandlers.ofString());
    }
    @Test void publicLookupAndAnonymousMutationRules() throws Exception {
        assertEquals(200,call("GET","/api/inventory/1",null,null).statusCode());
        assertEquals(401,call("POST","/api/inventory/reservations",null,"{}").statusCode());
        assertEquals(401,call("PUT","/api/inventory/1/stock",null,"{}").statusCode());
        assertEquals(401,call("GET","/api/inventory/reservations/"+UUID.randomUUID(),null,null).statusCode());
    }
    @Test void validTokenWithoutPermissionIsForbidden() throws Exception {
        assertEquals(403,call("POST","/api/inventory/reservations",token(""),"{}").statusCode());
        assertEquals(403,call("PUT","/api/inventory/1/stock",token("inventory.reserve"),"{}").statusCode());
        assertEquals(403,call("POST","/api/inventory/reservations",token("inventory.stock.write"),"{}").statusCode());
    }
    @Test void invalidIssuerAudienceSignatureExpiryAndMissingExpiryAreRejected() throws Exception {
        for(var invalid:new String[]{
                sign("inventory.reserve","wrong","Inventory.API",Instant.now().plusSeconds(300),KEYS,true),
                sign("inventory.reserve","Inventory.Tests","wrong",Instant.now().plusSeconds(300),KEYS,true),
                sign("inventory.reserve","Inventory.Tests","Inventory.API",Instant.now().minusSeconds(300),KEYS,true),
                sign("inventory.reserve","Inventory.Tests","Inventory.API",Instant.now().plusSeconds(300),keys(),true),
                sign("inventory.reserve","Inventory.Tests","Inventory.API",Instant.now().plusSeconds(300),KEYS,false),
                "not-a-jwt"}) {
            assertEquals(401,call("POST","/api/inventory/reservations",invalid,"{}").statusCode());
        }
    }
    @Test void authorizedReservationAndStockTokensReachTheirOperations() throws Exception {
        // Invalid payloads return 400 only AFTER successful authentication/authorization.
        assertEquals(400,call("PUT","/api/inventory/1/stock",token("inventory.stock.write"),"{}").statusCode());
        var id=UUID.randomUUID();
        var request="{\"reservationId\":\""+id+"\",\"items\":[{\"productId\":1,\"quantity\":1}]}";
        assertEquals(200,call("POST","/api/inventory/reservations",token("inventory.reserve"),request).statusCode());
        assertEquals(200,call("GET","/api/inventory/reservations/"+id,token("inventory.reserve"),null).statusCode());
        assertEquals(200,call("POST","/api/inventory/reservations/"+id+"/release",token("inventory.reserve"),null).statusCode());
    }
}
