using ApartmentAPI.DTOs; // ApiMessages, PaginationQuery.
using ApartmentAPI.Services; // IRoleService — CRUD vai trò Identity.
using ApartmentAPI.V1.DTOs; // Role DTO (create/update/list).
using ApartmentAPI.Versioning; // ApiVersionRouteValues.WithVersion.
using Asp.Versioning; // [ApiVersion] trên controller.
using ApartmentAPI.Authorization; // ApiAuthorization — chuỗi role thống nhất với seed Identity.
using Microsoft.AspNetCore.Authorization; // [Authorize(Roles = ...)] — phân quyền endpoint.
using Microsoft.AspNetCore.Mvc; // ControllerBase, IActionResult.

namespace ApartmentAPI.V1.Controllers;

// Vai trò Identity — CRUD không lọc theo CreatedAt (role ít): phân trang theo NameContains. Chỉ Admin.
[ApiController]
[ApiVersion("1.0")]
[Authorize(Roles = ApiAuthorization.AdminOnly)]
[Route("api/v{version:apiVersion}/roles")]
public class RolesController : ControllerBase
{
    private readonly IRoleAppService _service;

    public RolesController(IRoleAppService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] string? nameContains = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetPagedAsync(p, s, nameContains, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.RoleListSuccess, data });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var data = await _service.GetByIdAsync(id, ct);
        return Ok(new { message = ApiMessages.RoleGetSuccess, data });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoleDto dto, CancellationToken ct)
    {
        var data = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), ApiVersionRouteValues.WithVersion(this, new { id = data.Id }), new { message = ApiMessages.Ok, data });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRoleDto dto, CancellationToken ct)
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
