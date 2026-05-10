using ApartmentAPI.DTOs; // ApiMessages, PaginationQuery.
using ApartmentAPI.Entities; // AttachmentScope.
using ApartmentAPI.Services; // IAttachmentService.
using ApartmentAPI.Validators; // CreatedAtRangeQuery.
using ApartmentAPI.V1.DTOs; // Form model upload — dùng chung V1 types (multipart).
using ApartmentAPI.Versioning; // ApiVersionRouteValues.WithVersion.
using Asp.Versioning; // [ApiVersion].
using ApartmentAPI.Authorization; // ApiAuthorization — chuỗi role thống nhất với seed Identity.
using Microsoft.AspNetCore.Authorization; // [Authorize(Roles = ...)] — Admin hoặc User.
using Microsoft.AspNetCore.Mvc; // ControllerBase.

namespace ApartmentAPI.V2.Controllers;

// Đính kèm V2 — cùng layout route với V1; chỉ khác segment api/v2/... trong URL. Admin hoặc User.
[ApiController]
[ApiVersion("2.0")]
[Authorize(Roles = ApiAuthorization.AdminOrUser)]
[Route("api/v{version:apiVersion}/attachments")]
public class AttachmentsController : ControllerBase
{
    private readonly IAttachmentService _service;

    public AttachmentsController(IAttachmentService service) => _service = service;

    private string? DeletedBy() => User.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] string? originalFileNameContains = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetPagedAsync(
            p, s, createdAtFrom, createdAtTo, null, null, null, originalFileNameContains, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.AttachmentListSuccess, data });
    }

    [HttpGet("avatars")]
    public async Task<IActionResult> GetAvatars(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] string? originalFileNameContains = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetPagedAsync(
            p, s, createdAtFrom, createdAtTo, null, null, AttachmentScope.Avatar, originalFileNameContains, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.AttachmentListSuccess, data });
    }

    [HttpGet("users/{userId:guid}")]
    public async Task<IActionResult> GetByUser(
        Guid userId,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] string? originalFileNameContains = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetPagedAsync(
            p, s, createdAtFrom, createdAtTo, userId, null, null, originalFileNameContains, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.AttachmentListSuccess, data });
    }

    [HttpGet("feedbacks/{feedbackId:guid}")]
    public async Task<IActionResult> GetByFeedback(
        Guid feedbackId,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] string? originalFileNameContains = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetPagedAsync(
            p, s, createdAtFrom, createdAtTo, null, feedbackId, null, originalFileNameContains, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.AttachmentListSuccess, data });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var data = await _service.GetByIdAsync(id, ct);
        return Ok(new { message = ApiMessages.AttachmentGetSuccess, data });
    }

    [HttpPost("users/{userId:guid}/avatar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(35 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 35 * 1024 * 1024)]
    public async Task<IActionResult> CreateAvatarForUser(Guid userId, [FromForm] AvatarAttachmentUploadModel model, CancellationToken ct)
    {
        var data = await _service.CreateAvatarForUserAsync(userId, model.File, ct);
        return CreatedAtAction(nameof(GetById), ApiVersionRouteValues.WithVersion(this, new { id = data.Id }), new { message = ApiMessages.Ok, data });
    }

    [HttpPost("feedbacks/{feedbackId:guid}/files")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(35 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 35 * 1024 * 1024)]
    public async Task<IActionResult> CreateForFeedback(Guid feedbackId, [FromForm] FeedbackAttachmentUploadModel model, CancellationToken ct)
    {
        var data = await _service.CreateForFeedbackAsync(feedbackId, model.File, ct);
        return CreatedAtAction(nameof(GetById), ApiVersionRouteValues.WithVersion(this, new { id = data.Id }), new { message = ApiMessages.Ok, data });
    }

    [HttpPut("{id:guid}/avatar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(35 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 35 * 1024 * 1024)]
    public async Task<IActionResult> UpdateAvatar(Guid id, [FromForm] UpdateAvatarAttachmentFormModel model, CancellationToken ct)
    {
        await _service.UpdateAvatarAsync(id, model.File, ct);
        return Ok(new { message = ApiMessages.Ok });
    }

    [HttpPut("{id:guid}/feedback")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(35 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 35 * 1024 * 1024)]
    public async Task<IActionResult> UpdateFeedbackAttachment(Guid id, [FromForm] UpdateFeedbackAttachmentFormModel model, CancellationToken ct)
    {
        await _service.UpdateFeedbackAttachmentAsync(id, model.FeedbackId!.Value, model.File, ct);
        return Ok(new { message = ApiMessages.Ok });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        await _service.SoftDeleteAsync(id, DeletedBy(), ct);
        return Ok(new { message = ApiMessages.Ok });
    }
}
