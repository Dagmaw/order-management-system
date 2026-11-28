package com.dagmawi.inventory;
import java.net.URI;
import java.net.http.*;
import java.time.Duration;
import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.web.server.LocalServerPort;
import org.springframework.test.context.ActiveProfiles;
import static org.junit.jupiter.api.Assertions.*;
@ActiveProfiles("test")
@SpringBootTest(webEnvironment=SpringBootTest.WebEnvironment.RANDOM_PORT)
class InventoryHealthTests extends InventoryJwtSupport {
    @LocalServerPort int port;
    HttpResponse<String> get(String path) throws Exception {
        return HttpClient.newHttpClient().send(HttpRequest.newBuilder(URI.create("http://localhost:"+port+path))
            .timeout(Duration.ofSeconds(10)).GET().build(),HttpResponse.BodyHandlers.ofString());
    }
    @Test void loadBalancerProbesArePublicAndDoNotExposeDatabaseDetails() throws Exception {
        for(var path:new String[]{"/actuator/health","/actuator/health/readiness","/actuator/health/liveness"}) {
            var response=get(path);assertEquals(200,response.statusCode());
            assertTrue(response.body().contains("UP"));assertFalse(response.body().contains("jdbc"));assertFalse(response.body().contains("components"));
        }
        assertNotEquals(200,get("/actuator/env").statusCode());
        assertNotEquals(200,get("/actuator/configprops").statusCode());
    }
}
