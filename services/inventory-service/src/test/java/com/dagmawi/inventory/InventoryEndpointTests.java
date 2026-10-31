package com.dagmawi.inventory;

import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.web.server.LocalServerPort;
import tools.jackson.databind.JsonNode;
import tools.jackson.databind.json.JsonMapper;

import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

@SpringBootTest(
        webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT
)
class InventoryEndpointTests {

    @LocalServerPort
    private int port;

    private final JsonMapper jsonMapper = JsonMapper.builder().build();

    private final HttpClient client = HttpClient.newBuilder()
            .connectTimeout(Duration.ofSeconds(5))
            .build();

    private HttpResponse<String> get(String path) throws Exception {
        HttpRequest request = HttpRequest.newBuilder()
                .uri(URI.create("http://localhost:" + port + path))
                .timeout(Duration.ofSeconds(10))
                .GET()
                .build();

        return client.send(
                request,
                HttpResponse.BodyHandlers.ofString()
        );
    }

    @Test
    void knownProductReturns200AndInventoryJson() throws Exception {
        HttpResponse<String> response = get("/api/inventory/1");

        assertEquals(200, response.statusCode());

        JsonNode body = jsonMapper.readTree(response.body());

        assertEquals(1L, body.get("productId").asLong());
        assertEquals(100, body.get("availableQuantity").asInt());
    }

    @Test
    void missingProductReturns404ProblemResponse() throws Exception {
        HttpResponse<String> response =
                get("/api/inventory/999999");

        assertEquals(404, response.statusCode());

        JsonNode body = jsonMapper.readTree(response.body());

        assertEquals(404, body.get("status").asInt());
        assertEquals(
                "Inventory not found",
                body.get("title").asString()
        );

        assertTrue(
                response.headers()
                        .firstValue("Content-Type")
                        .orElse("")
                        .contains("application/problem+json")
        );
    }

    @Test
    void invalidProductIdReturns400() throws Exception {
        HttpResponse<String> response = get("/api/inventory/0");

        assertEquals(400, response.statusCode());

        JsonNode body = jsonMapper.readTree(response.body());

        assertEquals(400, body.get("status").asInt());
    }

    @Test
    void nonNumericProductIdReturns400() throws Exception {
        HttpResponse<String> response = get("/api/inventory/abc");

        assertEquals(400, response.statusCode());
    }
}