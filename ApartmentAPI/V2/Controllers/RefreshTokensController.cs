using ApartmentAPI.DTOs; // ApiMessages, PaginationQuery.
using ApartmentAPI.Services; // IRefreshTokenService — quản lý refresh token (phân trang, xóa mềm).
using ApartmentAPI.Validators; // CreatedAtRangeQuery cho bộ lọc ngày tạo.
using ApartmentAPI.V1.DTOs; // DTO refresh token phiên bản 1.
using ApartmentAPI.Versioning; // ApiVersionRouteValues.WithVersion cho CreatedAtAction.
using Asp.Versioning; // [ApiVersion] segment URL.
using ApartmentAPI.Authorization; // ApiAuthorization — chuỗi role thống nhất với seed Identity.
using Microsoft.AspNetCore.Authorization; // [Authorize(Roles = ...)] — phân quyền endpoint.
using Microsoft.AspNetCore.Mvc; // ControllerBase, IActionResult.

namespace ApartmentAPI.V2.Controllers;

// Refresh token — hỗ trợ quản trị / debug: phân trang tổng và theo user; xóa mềm bản ghi hash. Chỉ Admin.
[ApiController]
[ApiVersion("2.0")]
[Authorize(Roles = ApiAuthorization.AdminOnly)]
[Route("api/v{version:apiVersion}/refresh-tokens")]
public class RefreshTokensController : ControllerBase
{
    private readonly IRefreshTokenService _service;

    public RefreshTokensController(IRefreshTokenService service) => _service = service;

    private string? DeletedBy() => User.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] bool? isRevoked = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetPagedAsync(p, s, createdAtFrom, createdAtTo, null, isRevoked, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.RefreshTokenListSuccess, data });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var data = await _service.GetByIdAsync(id, ct);
        return Ok(new { message = ApiMessages.RefreshTokenGetSuccess, data });
    }

    [HttpGet("by-user/{userId:guid}")]
    public async Task<IActionResult> GetByUser(
        Guid userId,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] bool? isRevoked = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetPagedAsync(p, s, createdAtFrom, createdAtTo, userId, isRevoked, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.RefreshTokenListSuccess, data });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRefreshTokenDto dto, CancellationToken ct)
    {
        var data = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), ApiVersionRouteValues.WithVersion(this, new { id = data.Id }), new { message = ApiMessages.Ok, data });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRefreshTokenDto dto, CancellationToken ct)
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
