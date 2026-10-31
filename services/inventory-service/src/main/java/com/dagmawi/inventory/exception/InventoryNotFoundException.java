package com.dagmawi.inventory.exception;

public class InventoryNotFoundException extends RuntimeException {
    public InventoryNotFoundException(long productId) {
        super("Inventory was not found for product " + productId + ".");
    }
}
