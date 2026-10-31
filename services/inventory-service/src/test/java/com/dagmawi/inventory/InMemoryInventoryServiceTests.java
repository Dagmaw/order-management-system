package com.dagmawi.inventory;

import com.dagmawi.inventory.dto.InventoryResponse;
import com.dagmawi.inventory.exception.InvalidProductIdException;
import com.dagmawi.inventory.exception.InventoryNotFoundException;
import com.dagmawi.inventory.service.InMemoryInventoryService;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

public class InMemoryInventoryServiceTests {

    private final InMemoryInventoryService service = new InMemoryInventoryService();

    @Test
    void KnownProductReturnsInventory() {

        //Act
        InventoryResponse result = service.getInventory(1L);

        //Assert
        assertEquals(1L, result.productId());
        assertEquals(100, result.availableQuantity());
    }

    @Test
    void zeroStockProductStillExists() {
        //Act
        InventoryResponse result = service.getInventory(3L);

        //Assert
        assertEquals(3L, result.productId());
        assertEquals(0, result.availableQuantity());
    }

    @Test
    void missingProductThrowsNotFound() {
        assertThrows(
                InventoryNotFoundException.class,
                () -> service.getInventory(999999L)
        );
    }

    @Test
    void nonPositiveIdsAreRejected() {
        assertThrows(
                InvalidProductIdException.class,
                () -> service.getInventory(0L)
        );

        assertThrows(
                InvalidProductIdException.class,
                () -> service.getInventory(-1L)
        );
    }
}
