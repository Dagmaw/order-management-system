using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderManagement.Application.DTOs;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Interfaces;
using OrderManagement.Domain.Entities;
using OrderManagement.Infrastructure.Data;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;


namespace OrderManagement.Infrastructure.Identity;

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly JwtOptions _jwtOptions;

    public AuthService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IOptions<JwtOptions> jwtOptions)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtOptions = jwtOptions.Value;
    }

    public async Task<UserDto> RegisterAsync(
        RegisterDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var validationResults =
            new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var isValid =
            System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
                dto,
                new System.ComponentModel.DataAnnotations.ValidationContext(dto),
                validationResults,
                validateAllProperties: true);

        if (!isValid)
        {
            throw new RegistrationValidationException(
                validationResults.Select(
                    r => r.ErrorMessage ?? "Invalid registration input."));
        }

        var name = dto.Name.Trim();
        var email = dto.Email.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new RegistrationValidationException(
                new[] { "Name cannot be blank." });
        }

        var domainEmailExists = await _db.DomainUsers.AnyAsync(
            u => u.Email.ToLower() == email,
            cancellationToken);

        var existingAccount =
            await _userManager.FindByEmailAsync(email);

        if (domainEmailExists || existingAccount is not null)
        {
            throw new DuplicateEmailException();
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var user = new User
            {
                Name = name,
                Email = email
            };

            _db.DomainUsers.Add(user);

            await _db.SaveChangesAsync(cancellationToken);

            var account = new ApplicationUser
            {
                UserName = email,
                Email = email,
                UserId = user.Id,
                User = user
            };

            var createResult = await _userManager.CreateAsync(
                account,
                dto.Password);

            if (!createResult.Succeeded)
            {
                if (createResult.Errors.Any(
                    e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
                {
                    throw new DuplicateEmailException();
                }

                throw new RegistrationValidationException(
                    createResult.Errors.Select(e => e.Description));
            }

            var roleResult = await _userManager.AddToRoleAsync(
                account,
                "Customer");

            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Unable to assign the Customer role.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            await transaction.CommitAsync(cancellationToken);

            return new UserDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                CreatedAt = user.CreatedAt
            };
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation &&
            pg.ConstraintName is
                "IX_Users_Email" or
                "EmailIndex" or
                "UserNameIndex")
        {
            throw new DuplicateEmailException();
        }
    }


    public async Task<LoginResponseDto?> LoginAsync(
    LoginDto dto,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrEmpty(dto.Password) ||
            dto.Email.Length > 256 ||
            dto.Password.Length > 128)
        {
            return null;
        }

        var email = dto.Email.Trim().ToLowerInvariant();

        var account = await _userManager.FindByEmailAsync(email);

        if (account is null)
        {
            return null;
        }

        var signInResult =
            await _signInManager.CheckPasswordSignInAsync(
                account,
                dto.Password,
                lockoutOnFailure: true);

        if (!signInResult.Succeeded)
        {
            return null;
        }

        var userExists = await _db.DomainUsers.AnyAsync(
            u => u.Id == account.UserId,
            cancellationToken);

        if (!userExists)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(account);

        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(
            _jwtOptions.AccessTokenMinutes);

        var claims = new List<Claim>
    {
        new(
            JwtRegisteredClaimNames.Sub,
            account.Id),

        new(
            JwtRegisteredClaimNames.Jti,
            Guid.NewGuid().ToString()),

        new(
            "user_id",
            account.UserId.ToString(
                CultureInfo.InvariantCulture))
    };

        foreach (var role in roles)
        {
            claims.Add(new Claim("role", role));
        }

        var signingKey = new SymmetricSecurityKey(
            Convert.FromBase64String(
                _jwtOptions.SigningKeyBase64));

        var credentials = new SigningCredentials(
            signingKey,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var accessToken =
            new JwtSecurityTokenHandler().WriteToken(token);

        return new LoginResponseDto
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresAtUtc = expiresAt,
            UserId = account.UserId,
            Email = account.Email ?? email,
            Roles = roles.ToArray()
        };
    }
}