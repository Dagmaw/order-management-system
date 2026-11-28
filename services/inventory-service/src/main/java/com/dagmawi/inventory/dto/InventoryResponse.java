package com.dagmawi.inventory.dto;
public record InventoryResponse(long productId, int availableQuantity, long version) {
    public InventoryResponse(long productId, int availableQuantity) {
        this(productId, availableQuantity, 0);
    }
}
