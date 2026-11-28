package com.dagmawi.inventory.exception;


import org.springframework.http.HttpStatus;
import org.springframework.http.ProblemDetail;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;

@RestControllerAdvice
public class ApiExceptionHandler {
    @ExceptionHandler(InventoryOperationException.class)
    public ProblemDetail handleOperation(InventoryOperationException exception) {
        var problem=ProblemDetail.forStatusAndDetail(HttpStatus.valueOf(exception.getStatus()),exception.getMessage());
        problem.setTitle("Inventory operation rejected");
        return problem;
    }


    @ExceptionHandler(InventoryNotFoundException.class)
    public ProblemDetail handleNotFound(
            InventoryNotFoundException exception) {
        ProblemDetail problem = ProblemDetail.forStatusAndDetail(HttpStatus.NOT_FOUND, exception.getMessage());

        problem.setTitle("Inventory not found");

        return problem;
    }

    @ExceptionHandler(InvalidProductIdException.class)
    public ProblemDetail handleInvalidId(
            InvalidProductIdException exception) {
        ProblemDetail problem = ProblemDetail.forStatusAndDetail(
                HttpStatus.BAD_REQUEST,
                exception.getMessage()
        );

        problem.setTitle("Invalid product ID");

        return problem;
    }
}
