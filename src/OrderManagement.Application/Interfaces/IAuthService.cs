using OrderManagement.Application.DTOs;

namespace OrderManagement.Application.Interfaces;

public interface IAuthService
{
    Task<UserDto> RegisterAsync(
        RegisterDto dto,
        CancellationToken cancellationToken = default);

    Task<LoginResponseDto?> LoginAsync(
        LoginDto dto,
        CancellationToken cancellationToken = default);
}