using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Interfaces
{
    public interface IProductRepository
    {
        Task<IReadOnlyList<Product>> GetAllAsync(
               CancellationToken cancellationToken = default);
        Task<Product?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken = default);
        Task<Product> AddAsync(
            Product product,
            CancellationToken cancellationToken= default);
    }
}
