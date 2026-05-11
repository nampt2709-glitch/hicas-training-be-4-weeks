using ApartmentAPI.DTOs; // ApiMessages, PaginationQuery.
using ApartmentAPI.Services; // IAttachmentService — CRUD đính kèm.
using ApartmentAPI.Validators; // CreatedAtRangeQuery.
using ApartmentAPI.V1.DTOs; // Multipart model theo scope.
using ApartmentAPI.Versioning; // ApiVersionRouteValues.WithVersion.
using Asp.Versioning; // [ApiVersion].
using ApartmentAPI.Authorization; // ApiAuthorization — chuỗi role thống nhất với seed Identity.
using Microsoft.AspNetCore.Authorization; // [Authorize(Roles = ...)] — Admin hoặc User.
using Microsoft.AspNetCore.Mvc; // ControllerBase, FromForm.

namespace ApartmentAPI.V1.Controllers;

// V1 — CRUD cơ bản: danh sách/ chi tiết, POST avatar hoặc file feedback, xóa mềm. GET lọc theo user/feedback/avatars, PUT chuyên biệt → API v2.0.
[ApiController]
[ApiVersion("1.0")]
[Authorize(Roles = ApiAuthorization.AdminOrUser)]
[Route("api/v{version:apiVersion}/attachments")]
public class AttachmentsController : ControllerBase
{
    // Nghiệp vụ + lưu file — inject một lần trong constructor.
    private readonly IAttachmentService _service;

    public AttachmentsController(IAttachmentService service) => _service = service;

    // Tên người xóa mềm (audit soft delete).
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
    { // Mở action — toàn bộ đính kèm (mọi scope) phân trang.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetPagedAsync(
            p, s, createdAtFrom, createdAtTo, null, null, null, originalFileNameContains, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.AttachmentListSuccess, data });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    { // Mở action — chi tiết một attachment.
        var data = await _service.GetByIdAsync(id, ct);
        return Ok(new { message = ApiMessages.AttachmentGetSuccess, data });
    }

    [HttpPost("users/{userId:guid}/avatar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(35 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 35 * 1024 * 1024)]
    public async Task<IActionResult> CreateAvatarForUser(Guid userId, [FromForm] AvatarAttachmentUploadModel model, CancellationToken ct)
    { // Mở action — tạo avatar cho user (UserId trong route; form chỉ có file).
        var data = await _service.CreateAvatarForUserAsync(userId, model.File, ct);
        return CreatedAtAction(nameof(GetById), ApiVersionRouteValues.WithVersion(this, new { id = data.Id }), new { message = ApiMessages.Ok, data });
    }

    [HttpPost("feedbacks/{feedbackId:guid}/files")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(35 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 35 * 1024 * 1024)]
    public async Task<IActionResult> CreateForFeedback(Guid feedbackId, [FromForm] FeedbackAttachmentUploadModel model, CancellationToken ct)
    { // Mở action — tạo file đính kèm một feedback (tác giả file lấy từ feedback).
        var data = await _service.CreateForFeedbackAsync(feedbackId, model.File, ct);
        return CreatedAtAction(nameof(GetById), ApiVersionRouteValues.WithVersion(this, new { id = data.Id }), new { message = ApiMessages.Ok, data });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    { // Mở action — xóa mềm attachment.
        await _service.SoftDeleteAsync(id, DeletedBy(), ct);
        return Ok(new { message = ApiMessages.Ok });
    }
}
