package com.dagmawi.inventory;

import com.dagmawi.inventory.dto.*;
import com.dagmawi.inventory.exception.InventoryOperationException;
import com.dagmawi.inventory.service.ReservationService;
import org.junit.jupiter.api.*;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.web.server.LocalServerPort;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.context.ActiveProfiles;
import java.util.*;
import java.util.concurrent.*;
import java.net.URI;
import java.net.http.*;
import java.time.Duration;
import static org.junit.jupiter.api.Assertions.*;

@ActiveProfiles("test")
@SpringBootTest(webEnvironment=SpringBootTest.WebEnvironment.RANDOM_PORT,
        properties={"inventory.operations.enabled=true","springdoc.api-docs.enabled=true"})
class ReservationServiceTests extends InventoryJwtSupport {
    @Autowired ReservationService service;
    @Autowired JdbcTemplate jdbc;
    @LocalServerPort int port;

    @BeforeEach void fixtures() {
        jdbc.update("DELETE FROM inventory_reservation_item");
        jdbc.update("DELETE FROM inventory_reservation");
        jdbc.update("DELETE FROM inventory WHERE product_id>=100");
        jdbc.update("INSERT INTO inventory(product_id,available_quantity,version) VALUES (100,10,0),(101,1,0)");
    }
    ReserveInventoryRequest request(UUID id,int quantity) {
        return new ReserveInventoryRequest(id,List.of(new ReserveInventoryRequest.Item(100,quantity)));
    }
    int stock(long id) { return jdbc.queryForObject("SELECT available_quantity FROM inventory WHERE product_id=?",Integer.class,id); }
    void rejected(int status,Runnable action) {
        assertEquals(status,assertThrows(InventoryOperationException.class,action::run).getStatus());
    }
    @Test void reserveReplayAndReleaseAreIdempotent() {
        var id=UUID.randomUUID();
        assertEquals("RESERVED",service.reserve(request(id,3)).status());
        assertEquals(7,stock(100));
        service.reserve(request(id,3)); assertEquals(7,stock(100));
        assertEquals("RESERVED",service.get(id).status());
        service.release(id);service.release(id);assertEquals(10,stock(100));
        assertEquals("RELEASED",service.get(id).status());
        rejected(409,()->service.reserve(request(id,3)));
    }
    @Test void sameIdWithDifferentPayloadConflicts() {
        var id=UUID.randomUUID();service.reserve(request(id,2));
        rejected(409,()->service.reserve(request(id,3)));assertEquals(8,stock(100));
    }
    @Test void failedMultiItemReservationRollsBackEveryChange() {
        var id=UUID.randomUUID();
        rejected(409,()->service.reserve(new ReserveInventoryRequest(id,List.of(
                new ReserveInventoryRequest.Item(100,3),new ReserveInventoryRequest.Item(101,2)))));
        assertEquals(10,stock(100));assertEquals(1,stock(101));
        rejected(404,()->service.get(id));
    }
    @Test void missingProductRollsBackAndInvalidRequestsDoNotWrite() {
        rejected(404,()->service.reserve(new ReserveInventoryRequest(UUID.randomUUID(),List.of(
                new ReserveInventoryRequest.Item(100,1),new ReserveInventoryRequest.Item(999999,1)))));
        assertEquals(10,stock(100));
        rejected(400,()->service.reserve(request(UUID.randomUUID(),0)));
        rejected(400,()->service.reserve(new ReserveInventoryRequest(UUID.randomUUID(),List.of(
                new ReserveInventoryRequest.Item(100,1),new ReserveInventoryRequest.Item(100,1)))));
        var cancelled=UUID.randomUUID();
        assertEquals("RELEASED",service.release(cancelled).status());
        rejected(409,()->service.reserve(request(cancelled,1)));
        assertEquals(10,stock(100));
    }
    @Test void staleStockUpdateCannotOverwriteReservation() {
        service.reserve(request(UUID.randomUUID(),2));
        rejected(409,()->service.updateStock(100,new StockUpdateRequest(20,0L)));
        assertEquals(8,stock(100));
        var result=service.updateStock(100,new StockUpdateRequest(20,1L));
        assertEquals(2,result.version());assertEquals(20,stock(100));
        rejected(409,()->service.updateStock(100,new StockUpdateRequest(20,1L)));
        rejected(400,()->service.updateStock(100,new StockUpdateRequest(-1,2L)));
    }
    @Test void concurrentCustomersCannotOversell() throws Exception {
        try(var executor=Executors.newFixedThreadPool(12)) {
            var gate=new CountDownLatch(1);
            var tasks=new ArrayList<Future<Boolean>>();
            for(int i=0;i<20;i++)tasks.add(executor.submit(()->{
                gate.await();try {service.reserve(request(UUID.randomUUID(),1));return true;}
                catch(InventoryOperationException e){assertEquals(409,e.getStatus());return false;}
            }));
            gate.countDown();int successful=0;
            for(var task:tasks)if(task.get(30,TimeUnit.SECONDS))successful++;
            assertEquals(10,successful);assertEquals(0,stock(100));
        }
    }
    @Test void concurrentSameIdReservesOnlyOnceAndReleasesOnlyOnce() throws Exception {
        var id=UUID.randomUUID();
        try(var executor=Executors.newFixedThreadPool(8)) {
            var gate=new CountDownLatch(1);
            var tasks=new ArrayList<Future<ReservationResponse>>();
            for(int i=0;i<8;i++)tasks.add(executor.submit(()->{gate.await();return service.reserve(request(id,3));}));
            gate.countDown();for(var task:tasks)assertEquals("RESERVED",task.get(30,TimeUnit.SECONDS).status());
            assertEquals(7,stock(100));
            var releases=new ArrayList<Future<ReservationResponse>>();
            for(int i=0;i<8;i++)releases.add(executor.submit(()->service.release(id)));
            for(var task:releases)assertEquals("RELEASED",task.get(30,TimeUnit.SECONDS).status());
            assertEquals(10,stock(100));
        }
    }
    @Test void httpContractIncludesValidationAndDocumentedOperations() throws Exception {
        var client=HttpClient.newHttpClient();var id=UUID.randomUUID();
        var body="{\"reservationId\":\""+id+"\",\"items\":[{\"productId\":100,\"quantity\":2}]}";
        var response=client.send(HttpRequest.newBuilder(URI.create("http://localhost:"+port+"/api/inventory/reservations"))
                .timeout(Duration.ofSeconds(10)).header("Authorization","Bearer "+token("inventory.reserve")).header("Content-Type","application/json")
                .POST(HttpRequest.BodyPublishers.ofString(body)).build(),HttpResponse.BodyHandlers.ofString());
        assertEquals(200,response.statusCode());assertTrue(response.body().contains("RESERVED"));
        var invalid=client.send(HttpRequest.newBuilder(URI.create("http://localhost:"+port+"/api/inventory/100/stock"))
                .header("Authorization","Bearer "+token("inventory.stock.write")).header("Content-Type","application/json").PUT(HttpRequest.BodyPublishers.ofString("{}"))
                .build(),HttpResponse.BodyHandlers.ofString());
        assertEquals(400,invalid.statusCode());
        var doc=client.send(HttpRequest.newBuilder(URI.create("http://localhost:"+port+"/v3/api-docs")).GET().build(),HttpResponse.BodyHandlers.ofString());
        assertEquals(200,doc.statusCode());assertTrue(doc.body().contains("/api/inventory/reservations/{id}/release"));
    }
}
