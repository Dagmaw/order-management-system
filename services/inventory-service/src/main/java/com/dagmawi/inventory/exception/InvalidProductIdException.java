package com.dagmawi.inventory.exception;

public class InvalidProductIdException extends RuntimeException
{
    public InvalidProductIdException () {
        super("Product ID must be greater than zero.");
    }
}
