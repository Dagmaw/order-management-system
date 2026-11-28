package com.dagmawi.inventory.exception;
public class InventoryOperationException extends RuntimeException {
    private final int status;
    public InventoryOperationException(int status, String message) { super(message); this.status=status; }
    public int getStatus() { return status; }
}
