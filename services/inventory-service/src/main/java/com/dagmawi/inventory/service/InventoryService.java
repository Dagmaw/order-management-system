package com.dagmawi.inventory.service;

import com.dagmawi.inventory.dto.InventoryResponse;

public interface InventoryService {
    InventoryResponse getInventory(long productId);
}
