package com.dagmawi.inventory.service;
import java.util.Map;

import com.dagmawi.inventory.dto.InventoryResponse;
import com.dagmawi.inventory.exception.InvalidProductIdException;
import com.dagmawi.inventory.exception.InventoryNotFoundException;
import org.springframework.stereotype.Service;

@Service
public class InMemoryInventoryService implements InventoryService {
    private final Map<Long, InventoryResponse> inventory = Map.of(
            1L, new InventoryResponse(1L, 100),
            2L, new InventoryResponse(2L, 50),
            3L, new InventoryResponse(3L, 0)
    );

    @Override
    public InventoryResponse getInventory(long productId) {
        if(productId <= 0) {
            throw new InvalidProductIdException();
        }

        InventoryResponse result = inventory.get(productId);

        if(result == null) {
            throw new InventoryNotFoundException(productId);
        }

        return result;
    }
}
