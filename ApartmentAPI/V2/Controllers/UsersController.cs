using ApartmentAPI.DTOs; // ApiMessages, PaginationQuery dùng chung controller.
using ApartmentAPI.Services; // IUserService — nghiệp vụ người dùng Identity.
using ApartmentAPI.Validators; // CreatedAtRangeQuery nếu có lọc ngày.
using ApartmentAPI.V1.DTOs; // User DTO phiên bản 1 (create/update/list).
using ApartmentAPI.Versioning; // ApiVersionRouteValues.WithVersion cho CreatedAtAction.
using Asp.Versioning; // [ApiVersion] khai báo segment version.
using ApartmentAPI.Authorization; // ApiAuthorization — chuỗi role thống nhất với seed Identity.
using Microsoft.AspNetCore.Authorization; // [Authorize(Roles = ...)] — bắt buộc claim role trên JWT.
using Microsoft.AspNetCore.Mvc; // ControllerBase, action results.

namespace ApartmentAPI.V2.Controllers;

// V2 — CRUD User Identity trên /api/v2/ (cùng tập endpoint V1; minh họa versioning). Phân trang đa điều kiện; DeleteAsync xóa cứng. Chỉ Admin.
[ApiController]
[ApiVersion("2.0")]
[Authorize(Roles = ApiAuthorization.AdminOnly)]
[Route("api/v{version:apiVersion}/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _service;

    public UsersController(IUserService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] string? userNameContains = null,
        [FromQuery] string? emailContains = null,
        [FromQuery] string? fullNameContains = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetPagedAsync(
            p, s, createdAtFrom, createdAtTo, userNameContains, emailContains, fullNameContains, isActive, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.UserListSuccess, data });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var data = await _service.GetByIdAsync(id, ct);
        return Ok(new { message = ApiMessages.UserGetSuccess, data });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto, CancellationToken ct)
    {
        var data = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), ApiVersionRouteValues.WithVersion(this, new { id = data.Id }), new { message = ApiMessages.Ok, data });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDto dto, CancellationToken ct)
    {
        await _service.UpdateAsync(id, dto, ct);
        return Ok(new { message = ApiMessages.Ok });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return Ok(new { message = ApiMessages.Ok });
    }
}
