package com.dagmawi.inventory;

import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.web.server.LocalServerPort;
import org.springframework.test.context.ActiveProfiles;
import tools.jackson.databind.json.JsonMapper;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import static org.junit.jupiter.api.Assertions.*;

@ActiveProfiles("test")
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT,
        properties = {"springdoc.api-docs.enabled=true", "springdoc.swagger-ui.enabled=true"})
class OpenApiEndpointTests {
    @LocalServerPort private int port;

    private HttpResponse<String> get(String path) throws Exception {
        return HttpClient.newHttpClient().send(HttpRequest.newBuilder()
                .uri(URI.create("http://localhost:" + port + path))
                .timeout(Duration.ofSeconds(20)).GET().build(), HttpResponse.BodyHandlers.ofString());
    }

    @Test
    void describesLookupAndServesSwaggerUi() throws Exception {
        var response = get("/v3/api-docs");
        assertEquals(200, response.statusCode());
        var document = JsonMapper.builder().build().readTree(response.body());
        assertEquals("Inventory Service API", document.path("info").path("title").asString());
        var operation = document.path("paths").path("/api/inventory/{productId}").path("get");
        assertFalse(operation.isMissingNode());
        assertTrue(operation.path("responses").has("200"));
        assertTrue(operation.path("responses").has("404"));
        var ui = get("/swagger-ui/index.html");
        assertEquals(200, ui.statusCode());
        assertTrue(ui.body().contains("swagger-ui"));
        assertEquals(200, get("/api/inventory/1").statusCode());
    }
}
