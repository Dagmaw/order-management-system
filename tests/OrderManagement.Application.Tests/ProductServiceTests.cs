using OrderManagement.Application.DTOs;
using OrderManagement.Application.Interfaces;
using OrderManagement.Application.Services;
using OrderManagement.Domain.Entities;
using Xunit;

namespace OrderManagement.Application.Tests;

public class ProductServiceTests
{
    [Fact]
    public async Task UpdateProductAsync_TrimsInputAndReturnsSavedProduct()
    {
        // Arrange
        var repository = new StubProductRepository
        {
            UpdateResult = new Product
            {
                Id = 4,
                Name = "Updated Product",
                Description = "Updated description",
                Price = 35m,
                CreatedAt = new DateTime(2026, 10, 24)
            }
        };

        var service = new ProductService(repository);

        var request = new UpdateProductDto
        {
            Name = "  Updated Product  ",
            Description = "  Updated description  ",
            Price = 35m
        };

        // Act
        var result = await service.UpdateProductAsync(4, request);

        // Assert: input is cleaned before reaching the repository.
        Assert.Equal(4, repository.LastId);
        Assert.Equal("Updated Product", repository.LastName);
        Assert.Equal("Updated description", repository.LastDescription);
        Assert.Equal(35m, repository.LastPrice);

        // Assert: the saved entity is mapped to the response correctly.
        Assert.NotNull(result);
        Assert.Equal(4, result.Id);
        Assert.Equal("Updated Product", result.Name);
        Assert.Equal("Updated description", result.Description);
        Assert.Equal(35m, result.Price);
        Assert.Equal(repository.UpdateResult.CreatedAt, result.CreatedAt);
    }

    [Fact]
    public async Task UpdateProductAsync_WhenProductMissing_ReturnsNull()
    {
        // Arrange
        var repository = new StubProductRepository
        {
            UpdateResult = null
        };

        var service = new ProductService(repository);

        var request = new UpdateProductDto
        {
            Name = "Missing Product",
            Description = "Test description",
            Price = 35m
        };

        // Act
        var result = await service.UpdateProductAsync(999999, request);

        // Assert
        Assert.Null(result);
        Assert.Equal(999999, repository.LastId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeleteProductAsync_ReturnsRepositoryOutcome(
        bool deleteResult)
    {
        // Arrange
        var repository = new StubProductRepository
        {
            DeleteResult = deleteResult
        };

        var service = new ProductService(repository);

        // Act
        var result = await service.DeleteProductAsync(4);

        // Assert
        Assert.Equal(deleteResult, result);
        Assert.Equal(4, repository.LastId);
    }

    private sealed class StubProductRepository : IProductRepository
    {
        public Product? UpdateResult { get; init; }
        public bool DeleteResult { get; init; }

        public int LastId { get; private set; }
        public string? LastName { get; private set; }
        public string? LastDescription { get; private set; }
        public decimal LastPrice { get; private set; }

        public Task<Product?> UpdateAsync(
            int id,
            string name,
            string description,
            decimal price,
            CancellationToken cancellationToken = default)
        {
            LastId = id;
            LastName = name;
            LastDescription = description;
            LastPrice = price;

            return Task.FromResult(UpdateResult);
        }

        public Task<bool> DeleteAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            LastId = id;

            return Task.FromResult(DeleteResult);
        }

        public Task<IReadOnlyList<Product>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Product?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Product> AddAsync(
            Product product,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}