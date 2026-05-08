using ApartmentAPI.V1.DTOs;
using ApartmentAPI.Services;
using Asp.Versioning;
using ApartmentAPI.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace ApartmentAPI.V2.Controllers;

// Dòng chi tiết hóa đơn: CRUD + danh sách theo InvoiceId.
[ApiController]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/invoice-items")]
public class InvoiceItemsController : ControllerBase
{
    private readonly IInvoiceItemService _service;

    public InvoiceItemsController(IInvoiceItemService service) => _service = service;

    private string? DeletedBy() => User.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var data = await _service.GetAllAsync(ct);
        return Ok(new { message = ApiMessages.Ok, data });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var data = await _service.GetByIdAsync(id, ct);
        return Ok(new { message = ApiMessages.Ok, data });
    }

    [HttpGet("by-invoice/{invoiceId:guid}")]
    public async Task<IActionResult> GetByInvoice(Guid invoiceId, CancellationToken ct)
    {
        var data = await _service.GetByInvoiceIdAsync(invoiceId, ct);
        return Ok(new { message = ApiMessages.Ok, data });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceItemDto dto, CancellationToken ct)
    {
        var data = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), ApiVersionRouteValues.WithVersion(this, new { id = data.Id }), new { message = ApiMessages.Ok, data });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateInvoiceItemDto dto, CancellationToken ct)
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
