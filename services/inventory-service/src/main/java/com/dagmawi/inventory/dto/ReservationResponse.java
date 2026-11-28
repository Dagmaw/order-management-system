package com.dagmawi.inventory.dto;
import java.util.List;
import java.util.UUID;
public record ReservationResponse(UUID reservationId, String status, List<ReserveInventoryRequest.Item> items) {}
