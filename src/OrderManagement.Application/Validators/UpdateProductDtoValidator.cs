using FluentValidation;
using OrderManagement.Application.DTOs;

namespace OrderManagement.Application.Validators;

public class UpdateProductDtoValidator
    : AbstractValidator<UpdateProductDto>
{
    public UpdateProductDtoValidator()
    {
        RuleFor(dto => dto.Name)
            .NotEmpty()
            .WithMessage("Product name is required.")
            .MaximumLength(200)
            .WithMessage("Product name must not exceed 200 characters.");

        RuleFor(dto => dto.Description)
            .MaximumLength(200)
            .WithMessage(
                "Product description must not exceed 200 characters.");

        RuleFor(dto => dto.Price)
            .InclusiveBetween(0.01m, 9999999999999999.99m)
            .WithMessage(
                "Product price must be between 0.01 and 9999999999999999.99.");
    }
}