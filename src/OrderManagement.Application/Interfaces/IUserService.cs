using OrderManagement.Application.DTOs;

namespace OrderManagement.Application.Interfaces;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> GetUsersAsync(
        CancellationToken cancellationToken = default);

    Task<UserDto?> GetUserByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<UserDto> CreateUserAsync(
        CreateUserDto dto,
        CancellationToken cancellationToken = default);
}