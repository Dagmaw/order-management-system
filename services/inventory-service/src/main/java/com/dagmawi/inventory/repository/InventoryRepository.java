package com.dagmawi.inventory.repository;

import com.dagmawi.inventory.entity.InventoryItem;
import org.springframework.data.jpa.repository.JpaRepository;


public interface InventoryRepository extends JpaRepository<InventoryItem, Long> {
}