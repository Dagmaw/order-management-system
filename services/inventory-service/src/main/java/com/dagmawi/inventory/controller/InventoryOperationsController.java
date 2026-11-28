package com.dagmawi.inventory.controller;
import com.dagmawi.inventory.dto.*;
import com.dagmawi.inventory.service.ReservationService;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.web.bind.annotation.*;
import io.swagger.v3.oas.annotations.Operation;
import java.util.UUID;

@RestController
@RequestMapping("/api/inventory")
@io.swagger.v3.oas.annotations.security.SecurityRequirement(name="InventoryBearer")
@ConditionalOnProperty(name="inventory.operations.enabled",havingValue="true")
public class InventoryOperationsController {
    private final ReservationService service;
    public InventoryOperationsController(ReservationService service) { this.service=service; }

    @PutMapping("/{productId}/stock")
    @Operation(summary="Set available stock with an expected version",description="Requires inventory.stock.write scope. Stale versions return 409.")
    public InventoryResponse update(@PathVariable long productId,@RequestBody StockUpdateRequest request) {
        return service.updateStock(productId,request);
    }
    @PostMapping("/reservations")
    @Operation(summary="Reserve stock atomically",description="Reuse the same UUID and items for a safe retry. Different items for an existing UUID return 409. Returns 200 for both creation and replay.")
    public ReservationResponse reserve(@RequestBody ReserveInventoryRequest request) { return service.reserve(request); }

    @GetMapping("/reservations/{id}")
    @Operation(summary="Get a reservation for reconciliation")
    public ReservationResponse get(@PathVariable UUID id) { return service.get(id); }

    @PostMapping("/reservations/{id}/release")
    @Operation(summary="Release reserved stock once",description="Repeated release is idempotent. An unknown UUID creates a RELEASED cancellation marker to block late reserve requests.")
    public ReservationResponse release(@PathVariable UUID id) { return service.release(id); }
}
