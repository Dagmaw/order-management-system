using Microsoft.EntityFrameworkCore;
using OrderManagement.Application.Interfaces;
using OrderManagement.Domain.Entities;
using OrderManagement.Infrastructure.Data;
using Npgsql;
using OrderManagement.Application.Exceptions;

namespace OrderManagement.Infrastructure.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly ApplicationDbContext _db;

        public OrderRepository(ApplicationDbContext db)
        {
            _db = db;
        }
        public async Task<Order> AddAsync(Order order, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(order);
            _db.Orders.Add(order);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is PostgresException postgres 
                && postgres.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                throw new DataConflictException(
                    "The user or a product is no longer available. Refresh and retry.");
            }
            
            return order;
        }

        public async Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken)
        {
            return await _db.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .OrderByDescending(o => o.CreatedAt)
                .ThenByDescending(o => o.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken)
        {
            return await _db.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        }
    }
}
