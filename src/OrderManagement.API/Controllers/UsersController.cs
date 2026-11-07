using Microsoft.AspNetCore.Mvc;
using OrderManagement.Application.DTOs;
using OrderManagement.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using OrderManagement.API.Security;

namespace OrderManagement.API.Controllers;

[Authorize(Roles = "Customer, Admin")]
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetUsersAsync(
        CancellationToken cancellationToken)
    {
        var users = await _userService.GetUsersAsync(
            cancellationToken);

        return Ok(users);
    }

    [HttpGet("{id:int}", Name = "GetUserById")]
    public async Task<ActionResult<UserDto>> GetUserByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin"))
        {
            if (!User.TryGetDomainUserId(out var currentUserId))
            {
                return Forbid();
            }

            if (id != currentUserId)
            {
                return NotFound();
            }
        }

        var user = await _userService.GetUserByIdAsync(
            id,
            cancellationToken);

        return user is null ? NotFound() : Ok(user);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUserAsync(
        [FromBody] CreateUserDto dto,
        CancellationToken cancellationToken)
    {
        var user = await _userService.CreateUserAsync(
            dto,
            cancellationToken);

        return CreatedAtRoute(
            "GetUserById",
            new { id = user.Id },
            user);
    }
}