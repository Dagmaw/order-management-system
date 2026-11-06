using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Application.DTOs;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Interfaces;


namespace OrderManagement.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<UserDto>> RegisterAsync(
        [FromBody] RegisterDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await _authService.RegisterAsync(
                dto,
                cancellationToken);

            return CreatedAtRoute(
                "GetUserById",
                new { id = user.Id },
                user);
        }
        catch (DuplicateEmailException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Email already registered",
                detail: ex.Message);
        }
        catch (RegistrationValidationException ex)
        {
            return BadRequest(
                new ValidationProblemDetails(
                    new Dictionary<string, string[]>
                    {
                        ["Registration"] = ex.Errors
                    })
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Registration validation failed."
                });
        }
    }
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponseDto>> LoginAsync(
        [FromBody] LoginDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(
            dto,
            cancellationToken);

        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        if (result is null)
        {
            Response.Headers.WWWAuthenticate = "Bearer";

            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Login failed",
                detail: "Unable to sign in with the supplied credentials.");
        }

        return Ok(result);
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult GetCurrentUser()
    {
        var roles = User.FindAll("role")
            .Select(claim => claim.Value)
            .ToArray();

        return Ok(new
        {
            accountId = User.FindFirst("sub")?.Value,
            userId = User.FindFirst("user_id")?.Value,
            roles
        });
    }
}