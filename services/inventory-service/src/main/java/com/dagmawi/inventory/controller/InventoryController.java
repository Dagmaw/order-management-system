package com.dagmawi.inventory.controller;

import com.dagmawi.inventory.dto.InventoryResponse;
import com.dagmawi.inventory.service.InventoryService;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;


@RestController
@RequestMapping("/api/inventory")
public class InventoryController {
    private  final InventoryService inventoryService;

    public InventoryController(InventoryService inventoryService)
    {
        this.inventoryService = inventoryService;
    }

    @io.swagger.v3.oas.annotations.Operation(
            summary = "Look up available stock",
            description = "Returns persisted stock for one product. This operation does not reserve stock.")
    @io.swagger.v3.oas.annotations.responses.ApiResponses({
            @io.swagger.v3.oas.annotations.responses.ApiResponse(responseCode = "200", description = "Inventory found"),
            @io.swagger.v3.oas.annotations.responses.ApiResponse(responseCode = "400", description = "Product ID must be positive",
                    content = @io.swagger.v3.oas.annotations.media.Content),
            @io.swagger.v3.oas.annotations.responses.ApiResponse(responseCode = "404", description = "Inventory not found",
                    content = @io.swagger.v3.oas.annotations.media.Content)
    })
    @GetMapping("/{productId}")
    public InventoryResponse getInventory (
          @PathVariable("productId") long productId) {
              return inventoryService.getInventory(productId);
    }
}
