using ApartmentAPI.DTOs; // ApiMessages, PaginationQuery.
using ApartmentAPI.Entities; // InvoiceStatus lọc danh sách.
using ApartmentAPI.Services; // IInvoiceService — hóa đơn và trạng thái.
using ApartmentAPI.Validators; // CreatedAtRangeQuery.
using ApartmentAPI.V1.DTOs; // DTO invoice phiên bản 1.
using ApartmentAPI.Versioning; // ApiVersionRouteValues.WithVersion.
using Asp.Versioning; // [ApiVersion].
using ApartmentAPI.Authorization; // ApiAuthorization — chuỗi role thống nhất với seed Identity.
using Microsoft.AspNetCore.Authorization; // [Authorize(Roles = ...)] — Admin hoặc User.
using Microsoft.AspNetCore.Mvc; // ControllerBase, IActionResult.

namespace ApartmentAPI.V1.Controllers;

// Hóa đơn — phân trang tổng / theo căn (một căn nhiều hóa đơn), chi tiết, tạo/sửa/xóa mềm. Admin hoặc User.
[ApiController]
[ApiVersion("1.0")]
[Authorize(Roles = ApiAuthorization.AdminOrUser)]
[Route("api/v{version:apiVersion}/invoices")]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _service;

    public InvoicesController(IInvoiceService service) => _service = service;

    private string? DeletedBy() => User.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] Guid? apartmentId = null,
        [FromQuery] InvoiceStatus? status = null,
        [FromQuery] string? invoiceCode = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetPagedAsync(
            p, s, createdAtFrom, createdAtTo, apartmentId, status, invoiceCode, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.InvoiceListSuccess, data });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var data = await _service.GetByIdAsync(id, ct);
        return Ok(new { message = ApiMessages.InvoiceGetSuccess, data });
    }

    [HttpGet("by-apartment/{apartmentId:guid}")]
    public async Task<IActionResult> GetByApartment(
        Guid apartmentId,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] InvoiceStatus? status = null,
        [FromQuery] string? invoiceCode = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetPagedAsync(
            p, s, createdAtFrom, createdAtTo, apartmentId, status, invoiceCode, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.InvoiceListSuccess, data });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceDto dto, CancellationToken ct)
    {
        var data = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), ApiVersionRouteValues.WithVersion(this, new { id = data.Id }), new { message = ApiMessages.Ok, data });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateInvoiceDto dto, CancellationToken ct)
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
