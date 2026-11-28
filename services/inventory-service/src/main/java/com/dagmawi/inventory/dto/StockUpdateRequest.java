package com.dagmawi.inventory.dto;
public record StockUpdateRequest(Integer availableQuantity, Long expectedVersion) {}
