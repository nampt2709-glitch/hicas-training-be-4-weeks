using ApartmentAPI.V1.DTOs;
using ApartmentAPI.Entities;
using ApartmentAPI.Services;
using Asp.Versioning;
using ApartmentAPI.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace ApartmentAPI.V1.Controllers;

// Metadata file đính kèm: CRUD + lọc theo user / feedback / scope / post.
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/attachments")]
public class AttachmentsController : ControllerBase
{
    private readonly IAttachmentService _service;

    public AttachmentsController(IAttachmentService service) => _service = service;

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

    [HttpGet("by-feedback/{feedbackId:guid}")]
    public async Task<IActionResult> GetByFeedback(Guid feedbackId, CancellationToken ct)
    {
        var data = await _service.GetByFeedbackIdAsync(feedbackId, ct);
        return Ok(new { message = ApiMessages.Ok, data });
    }

    [HttpGet("by-post/{postId:guid}")]
    public async Task<IActionResult> GetByPost(Guid postId, CancellationToken ct)
    {
        var data = await _service.GetByPostIdAsync(postId, ct);
        return Ok(new { message = ApiMessages.Ok, data });
    }

    [HttpGet("by-scope/{scope}")]
    public async Task<IActionResult> GetByScope(AttachmentScope scope, CancellationToken ct)
    {
        var data = await _service.GetByScopeAsync(scope, ct);
        return Ok(new { message = ApiMessages.Ok, data });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAttachmentDto dto, CancellationToken ct)
    {
        var data = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), ApiVersionRouteValues.WithVersion(this, new { id = data.Id }), new { message = ApiMessages.Ok, data });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAttachmentDto dto, CancellationToken ct)
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
