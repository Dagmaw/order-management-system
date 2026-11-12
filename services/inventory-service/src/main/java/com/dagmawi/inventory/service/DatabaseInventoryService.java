package com.dagmawi.inventory.service;

import com.dagmawi.inventory.dto.InventoryResponse;
import com.dagmawi.inventory.exception.InvalidProductIdException;
import com.dagmawi.inventory.exception.InventoryNotFoundException;
import com.dagmawi.inventory.repository.InventoryRepository;
import org.springframework.stereotype.Service;

@Service
public class DatabaseInventoryService implements InventoryService {

    private final InventoryRepository inventoryRepository;

    public DatabaseInventoryService(
            InventoryRepository inventoryRepository) {
        this.inventoryRepository = inventoryRepository;
    }

    @Override
    public InventoryResponse getInventory(long productId) {
        if (productId <= 0) {
            throw new InvalidProductIdException();
        }

        var item = inventoryRepository.findById(productId)
                .orElseThrow(() -> new InventoryNotFoundException(productId));

        return new InventoryResponse(
                item.getProductId(),
                item.getAvailableQuantity());
    }
}