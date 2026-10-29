

using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Interfaces
{
    public interface IUserRepository
    {
        Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default);  
        
        Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

        Task<User> AddAsync(User user, CancellationToken cancellationToken = default);
        Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    }
}
