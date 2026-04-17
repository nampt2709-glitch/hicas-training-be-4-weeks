using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CommentAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _service;

    public UsersController(IUserService service)
    {
        _service = service;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAll()
    {
        var result = await _service.GetAllAsync();
        return Ok(new { message = ApiMessages.UserListSuccess, data = result });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound() : Ok(new { message = ApiMessages.UserGetSuccess, data = result });
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
    {
        var result = await _service.CreateAsync(dto);
        if (result is null)
        {
            return Conflict(new { code = ApiErrorCodes.UserNameConflict, message = ApiMessages.UserNameTaken });
        }

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Id },
            new { message = ApiMessages.UserCreateSuccess, data = result });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDto dto)
    {
        var updated = await _service.UpdateAsync(id, dto);
        return updated ? Ok(new { message = ApiMessages.UserUpdateSuccess }) : NotFound();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _service.DeleteAsync(id);
        return deleted ? Ok(new { message = ApiMessages.UserDeleteSuccess }) : NotFound();
    }
}
