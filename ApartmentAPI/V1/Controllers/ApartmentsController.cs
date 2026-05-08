using ApartmentAPI.V1.DTOs;
using ApartmentAPI.Entities;
using ApartmentAPI.Services;
using Asp.Versioning;
using ApartmentAPI.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace ApartmentAPI.V1.Controllers;

// API căn hộ: danh sách, chi tiết, theo trạng thái, tạo/sửa/xóa mềm.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/apartments")]
public class ApartmentsController : ControllerBase
{
    private readonly IApartmentService _service;

    public ApartmentsController(IApartmentService service) => _service = service;

    private string? DeletedBy() => User.Identity?.Name;

    // Lấy toàn bộ căn hộ (chưa xóa mềm — filter global DbContext).
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var data = await _service.GetAllAsync(ct);
        return Ok(new { message = ApiMessages.Ok, data });
    }

    // Lấy một căn hộ theo Id.
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var data = await _service.GetByIdAsync(id, ct);
        return Ok(new { message = ApiMessages.Ok, data });
    }

    // Lấy danh sách căn hộ theo trạng thái (Available / Occupied / Maintenance).
    [HttpGet("by-status/{status}")]
    public async Task<IActionResult> GetByStatus(ApartmentStatus status, CancellationToken ct)
    {
        var data = await _service.GetByStatusAsync(status, ct);
        return Ok(new { message = ApiMessages.Ok, data });
    }

    // Tạo căn hộ mới (RoomNumber + Floor unique trong DB).
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApartmentDto dto, CancellationToken ct)
    {
        var data = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), ApiVersionRouteValues.WithVersion(this, new { id = data.Id }), new { message = ApiMessages.Ok, data });
    }

    // Cập nhật thông tin căn hộ.
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateApartmentDto dto, CancellationToken ct)
    {
        await _service.UpdateAsync(id, dto, ct);
        return Ok(new { message = ApiMessages.Ok });
    }

    // Xóa mềm căn hộ (IsDeleted = true).
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        await _service.SoftDeleteAsync(id, DeletedBy(), ct);
        return Ok(new { message = ApiMessages.Ok });
    }
}
