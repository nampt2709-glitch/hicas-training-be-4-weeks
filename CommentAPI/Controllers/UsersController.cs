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

    /// <summary>Danh sách user có phân trang; mặc định page=1, pageSize=20 nếu không gửi query.</summary>
    [HttpGet]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetPagedAsync(p, s, cancellationToken);
        return Ok(new { message = ApiMessages.UserListSuccess, data = result });
    }

    /// <summary>Tìm đúng một user theo Id — trả đủ trường như GET by id.</summary>
    [HttpGet("search/id/{id:guid}")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> SearchById(Guid id)
    {
        var result = await _service.GetByIdAsync(id);
        return Ok(new { message = ApiMessages.UserGetSuccess, data = result });
    }

    /// <summary>Tìm user theo <see cref="UserDto.Name"/> (chuỗi chứa).</summary>
    [HttpGet("search/by-name")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> SearchByName(
        [FromQuery] string? name,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.SearchByNamePagedAsync(name, p, s, cancellationToken);
        return Ok(new { message = ApiMessages.UserSearchByNameSuccess, data = result });
    }

    /// <summary>Tìm user theo <see cref="UserDto.UserName"/> (chuỗi chứa).</summary>
    [HttpGet("search/by-username")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> SearchByUserName(
        [FromQuery] string? userName,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.SearchByUserNamePagedAsync(userName, p, s, cancellationToken);
        return Ok(new { message = ApiMessages.UserSearchByUserNameSuccess, data = result });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _service.GetByIdAsync(id);
        return Ok(new { message = ApiMessages.UserGetSuccess, data = result });
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
    {
        var result = await _service.CreateAsync(dto);
        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Id },
            new { message = ApiMessages.UserCreateSuccess, data = result });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDto dto)
    {
        await _service.UpdateAsync(id, dto);
        return Ok(new { message = ApiMessages.UserUpdateSuccess });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.DeleteAsync(id);
        return Ok(new { message = ApiMessages.UserDeleteSuccess });
    }
}
