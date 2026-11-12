package com.dagmawi.inventory.entity;
import jakarta.persistence.Entity;
import jakarta.persistence.Column;
import jakarta.persistence.Table;
import jakarta.persistence.Id;

@Entity
@Table(name = "inventory")
public class InventoryItem
{
    @Id
    @Column(name = "product_id")
    private Long productId;

    @Column(name = "available_quantity", nullable = false)
    private Integer availableQuantity;

    protected InventoryItem() {}

    public long getProductId() {
        return productId;
    }

    public int getAvailableQuantity() {
        return availableQuantity;
    }
}
