package com.dagmawi.inventory.dto;
import java.util.List;
import java.util.UUID;
public record ReserveInventoryRequest(UUID reservationId, List<Item> items) {
    public record Item(long productId, int quantity) {}
}
