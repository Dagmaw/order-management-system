using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderManagement.Application.Exceptions;

using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Interfaces;
using OrderManagement.Domain.Entities;
using OrderManagement.Infrastructure.Data;

namespace OrderManagement.Infrastructure.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly ApplicationDbContext _db;
        public UserRepository(ApplicationDbContext db)
        {
            _db = db;   
        }

        public async Task<User> AddAsync(
        User user,
        CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(user);

            _db.Users.Add(user);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is PostgresException postgresException
                && postgresException.SqlState
                    == PostgresErrorCodes.UniqueViolation
                && postgresException.ConstraintName == "IX_Users_Email")
            {
                throw new DuplicateEmailException();
            }

            return user;
        }

        public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
        {
            return await _db.Users.AnyAsync(u => u.Email == email, cancellationToken);
        }

        public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken)
        {
            return await _db.Users
                .AsAsyncEnumerable()
                .OrderBy(u => u.Name)
                .ThenBy(user => user.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken)
        {
            return await _db.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        }
    }
}
