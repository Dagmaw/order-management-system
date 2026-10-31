package com.dagmawi.inventory.dto;

public record InventoryResponse(
        long productId,
        int availableQuantity
) {
}