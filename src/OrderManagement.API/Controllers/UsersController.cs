using Microsoft.AspNetCore.Mvc;
using OrderManagement.Application.DTOs;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Interfaces;

namespace OrderManagement.API.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

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
        var user = await _userService.GetUserByIdAsync(
            id,
            cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        return Ok(user);
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUserAsync(
        [FromBody] CreateUserDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await _userService.CreateUserAsync(
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
    }
}