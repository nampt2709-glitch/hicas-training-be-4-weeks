using ApartmentAPI.DTOs; // ApiMessages, PaginationQuery.
using ApartmentAPI.Entities; // ApartmentStatus cho route by-status.
using ApartmentAPI.Services; // IApartmentService CRUD + phân trang.
using ApartmentAPI.Validators; // CreatedAtRangeQuery, PaginationQuery.
using ApartmentAPI.V1.DTOs; // DTO chung căn hộ (V2 dùng lại projection V1 cho đến khi có DTO V2).
using ApartmentAPI.Versioning; // ApiVersionRouteValues.WithVersion → CreatedAtAction.
using Asp.Versioning; // ApiVersion attribute.
using ApartmentAPI.Authorization; // ApiAuthorization — chuỗi role thống nhất với seed Identity.
using Microsoft.AspNetCore.Authorization; // [Authorize(Roles = ...)] — Admin hoặc User dùng nghiệp vụ.
using Microsoft.AspNetCore.Mvc; // ControllerBase, route, HTTP verbs.

namespace ApartmentAPI.V2.Controllers;

// V2 — đầy đủ route so với V1: có GET by-status/...; cùng service/DTO, segment /api/v2/ (minh họa versioning). Admin hoặc User.
[ApiController]
[ApiVersion("2.0")]
[Authorize(Roles = ApiAuthorization.AdminOrUser)]
[Route("api/v{version:apiVersion}/apartments")]
public class ApartmentsController : ControllerBase
{
    private readonly IApartmentService _service; // Nghiệp vụ căn hộ + chiến lược cache list.

    public ApartmentsController(IApartmentService service) => _service = service;

    private string? DeletedBy() => User.Identity?.Name; // Audit soft delete.

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] string? roomNumber = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetPagedAsync(p, s, createdAtFrom, createdAtTo, null, roomNumber, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.ApartmentListSuccess, data });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var data = await _service.GetByIdAsync(id, ct);
        return Ok(new { message = ApiMessages.ApartmentGetSuccess, data });
    }

    [HttpGet("by-status/{status}")]
    public async Task<IActionResult> GetByStatus(
        ApartmentStatus status,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] string? roomNumber = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetPagedAsync(p, s, createdAtFrom, createdAtTo, status, roomNumber, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.ApartmentListSuccess, data });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApartmentDto dto, CancellationToken ct)
    {
        var data = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), ApiVersionRouteValues.WithVersion(this, new { id = data.Id }), new { message = ApiMessages.Ok, data });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateApartmentDto dto, CancellationToken ct)
    {
        await _service.UpdateAsync(id, dto, ct);
        return Ok(new { message = ApiMessages.Ok });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        await _service.SoftDeleteAsync(id, DeletedBy(), ct);
        return Ok(new { message = ApiMessages.Ok });
    }
}
