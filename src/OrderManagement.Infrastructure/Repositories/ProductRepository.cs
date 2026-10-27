using Microsoft.EntityFrameworkCore;

using OrderManagement.Application.Interfaces;

using OrderManagement.Domain.Entities;

using OrderManagement.Infrastructure.Data;

namespace OrderManagement.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly ApplicationDbContext _db;

    public ProductRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Product> AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product, nameof(product));
        _db.Products.Add(product);
        await _db.SaveChangesAsync(cancellationToken);
        return product; 
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Products
            .AsNoTracking()
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _db.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(
            product => product.Id == id, cancellationToken);
    }

    public async Task<Product?> UpdateAsync(int id, string name, string description, decimal price, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products
          .SingleOrDefaultAsync(product => product.Id == id, cancellationToken);

        if (product is null)
        {
            return null;
        }

        product.Name = name;
        product.Description = description;
        product.Price = price;

        await _db.SaveChangesAsync(cancellationToken);
        return product;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products
            .SingleOrDefaultAsync(product => product.Id == id, cancellationToken);

        if (product is null)
        {
            return false;
        }

        _db.Products.Remove(product);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
