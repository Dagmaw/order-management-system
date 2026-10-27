using OrderManagement.Application.DTOs; 

namespace OrderManagement.Application.Interfaces
{
    public interface IProductService
    {
        Task<IReadOnlyList<ProductDto>> GetProductDtosAsync(
            CancellationToken cancellationToken = default);

        Task<ProductDto?> GetProductByIdAsync(
            int id,
            CancellationToken cancellationToken = default);

        Task<ProductDto> CreateProductAsync(
            CreateProductDto dto,
            CancellationToken cancellationToken = default);

        Task<ProductDto?> UpdateProductAsync(
            int id,
            UpdateProductDto dto,
            CancellationToken cancellationToken = default);

        Task<bool> DeleteProductAsync(
            int id,
            CancellationToken cancellationToken = default);

    }
}
