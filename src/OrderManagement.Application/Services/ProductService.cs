using OrderManagement.Application.DTOs;
using OrderManagement.Application.Interfaces;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository _productRepository;

    public ProductService(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<IReadOnlyList<ProductDto>> GetProductDtosAsync(
        CancellationToken cancellationToken = default)
    {
        var products = await _productRepository.GetAllAsync(
            cancellationToken);

        return products
            .Select(ToDto)
            .ToList();
    }

    public async Task<ProductDto?> GetProductByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(
            id,
            cancellationToken);

        return product is null ? null : ToDto(product);
    }

    public async Task<ProductDto> CreateProductAsync(
        CreateProductDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var product = new Product
        {
            Name = dto.Name.Trim(),
            Description = dto.Description.Trim(),
            Price = dto.Price
        };

        var savedProduct = await _productRepository.AddAsync(
            product,
            cancellationToken);

        return ToDto(savedProduct);
    }

    private static ProductDto ToDto(Product product)
    {
        return new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            CreatedAt = product.CreatedAt
        };
    }

    public async Task<ProductDto?> UpdateProductAsync(int id, UpdateProductDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var product = await _productRepository.UpdateAsync(
            id,
            dto.Name.Trim(),
            dto.Description.Trim(),
            dto.Price,
            cancellationToken);

        return product is null ? null : ToDto(product);
    }

    public async Task<bool> DeleteProductAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _productRepository.DeleteAsync(id, cancellationToken);
    }
}