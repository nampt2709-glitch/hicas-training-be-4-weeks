using ApartmentAPI.V1.DTOs;
using ApartmentAPI.Services;
using Asp.Versioning;
using ApartmentAPI.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace ApartmentAPI.V1.Controllers;

// Phản hồi (cây cha–con): CRUD + theo user + chỉ các gốc (ParentId null).
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/feedbacks")]
public class FeedbacksController : ControllerBase
{
    private readonly IFeedbackService _service;

    public FeedbacksController(IFeedbackService service) => _service = service;

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

    [HttpGet("by-user/{userId:guid}")]
    public async Task<IActionResult> GetByUser(Guid userId, CancellationToken ct)
    {
        var data = await _service.GetByUserIdAsync(userId, ct);
        return Ok(new { message = ApiMessages.Ok, data });
    }

    [HttpGet("roots")]
    public async Task<IActionResult> GetRoots(CancellationToken ct)
    {
        var data = await _service.GetRootsAsync(ct);
        return Ok(new { message = ApiMessages.Ok, data });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFeedbackDto dto, CancellationToken ct)
    {
        var data = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), ApiVersionRouteValues.WithVersion(this, new { id = data.Id }), new { message = ApiMessages.Ok, data });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFeedbackDto dto, CancellationToken ct)
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
