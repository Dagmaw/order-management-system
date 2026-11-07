using FluentValidation.TestHelper;
using OrderManagement.Application.DTOs;
using OrderManagement.Application.Validators;

namespace OrderManagement.Application.Tests;

public class ProductValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_ReturnsNameError(string name)
    {
        // Arrange
        var validator = new CreateProductDtoValidator();

        var dto = new CreateProductDto
        {
            Name = name,
            Description = "Test description",
            Price = 25.00m
        };

        // Act
        var result = validator.TestValidate(dto);

        // Assert
        result.ShouldHaveValidationErrorFor(product => product.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_WithBlankName_ReturnsNameError(string name)
    {
        var validator = new UpdateProductDtoValidator();

        var dto = new UpdateProductDto
        {
            Name = name,
            Description = "Test description",
            Price = 25.00m
        };

        var result = validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(product => product.Name);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("10000000000000000")]
    public void Create_WithInvalidPrice_ReturnsPriceError(
        string price)
    {
        var validator = new CreateProductDtoValidator();

        var dto = new CreateProductDto
        {
            Name = "Test Product",
            Description = "Test description",
            Price = decimal.Parse(
                price,
                System.Globalization.CultureInfo.InvariantCulture)
        };

        var result = validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(product => product.Price);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("10000000000000000")]
    public void Update_WithInvalidPrice_ReturnsPriceError(
        string price)
    {
        var validator = new UpdateProductDtoValidator();

        var dto = new UpdateProductDto
        {
            Name = "Test Product",
            Description = "Test description",
            Price = decimal.Parse(
                price,
                System.Globalization.CultureInfo.InvariantCulture)
        };

        var result = validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(product => product.Price);
    }

    [Fact]
    public void Create_WithOverlongText_ReturnsErrors()
    {
        var validator = new CreateProductDtoValidator();

        var dto = new CreateProductDto
        {
            Name = new string('N', 201),
            Description = new string('D', 201),
            Price = 25.00m
        };

        var result = validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(product => product.Name);
        result.ShouldHaveValidationErrorFor(product => product.Description);
    }

    [Fact]
    public void Update_WithOverlongText_ReturnsErrors()
    {
        var validator = new UpdateProductDtoValidator();

        var dto = new UpdateProductDto
        {
            Name = new string('N', 201),
            Description = new string('D', 201),
            Price = 25.00m
        };

        var result = validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(product => product.Name);
        result.ShouldHaveValidationErrorFor(product => product.Description);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("9999999999999999.99")]
    public void Create_WithBoundaryValues_PassesValidation(
        string price)
    {
        var validator = new CreateProductDtoValidator();

        var dto = new CreateProductDto
        {
            Name = new string('N', 200),
            Description = new string('D', 200),
            Price = decimal.Parse(
                price,
                System.Globalization.CultureInfo.InvariantCulture)
        };

        var result = validator.TestValidate(dto);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("9999999999999999.99")]
    public void Update_WithBoundaryValues_PassesValidation(
        string price)
    {
        var validator = new UpdateProductDtoValidator();

        var dto = new UpdateProductDto
        {
            Name = new string('N', 200),
            Description = new string('D', 200),
            Price = decimal.Parse(
                price,
                System.Globalization.CultureInfo.InvariantCulture)
        };

        var result = validator.TestValidate(dto);

        result.ShouldNotHaveAnyValidationErrors();
    }
}