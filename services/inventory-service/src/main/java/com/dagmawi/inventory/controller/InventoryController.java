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

    @GetMapping("/{productId}")
    public InventoryResponse getInventory (
          @PathVariable("productId") long productId) {
              return inventoryService.getInventory(productId);
    }
}
