using OrderManagement.Application.DTOs;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Interfaces;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;

    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync(
        CancellationToken cancellationToken = default)
    {
        var users = await _userRepository.GetAllAsync(
            cancellationToken);

        return users
            .Select(ToDto)
            .ToList();
    }

    public async Task<UserDto?> GetUserByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(
            id,
            cancellationToken);

        return user is null ? null : ToDto(user);
    }

    public async Task<UserDto> CreateUserAsync(
        CreateUserDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var email = dto.Email.Trim().ToLowerInvariant();

        var emailExists = await _userRepository.EmailExistsAsync(
            email,
            cancellationToken);

        if (emailExists)
        {
            throw new DuplicateEmailException();
        }

        var user = new User
        {
            Name = dto.Name.Trim(),
            Email = email
        };

        var savedUser = await _userRepository.AddAsync(
            user,
            cancellationToken);

        return ToDto(savedUser);
    }

    private static UserDto ToDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            CreatedAt = user.CreatedAt
        };
    }
}