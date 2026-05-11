using ApartmentAPI.DTOs; // ApiMessages, PaginationQuery.
using ApartmentAPI.Services; // IAttachmentService — CRUD đính kèm.
using ApartmentAPI.Validators; // CreatedAtRangeQuery.
using Asp.Versioning; // [ApiVersion].
using ApartmentAPI.Authorization; // ApiAuthorization — chuỗi role thống nhất với seed Identity.
using Microsoft.AspNetCore.Authorization; // [Authorize(Roles = ...)] — Admin hoặc User.
using Microsoft.AspNetCore.Mvc; // ControllerBase.

namespace ApartmentAPI.V1.Controllers;

// V1 — chỉ CRUD cơ bản entity Attachment: danh sách phân trang, chi tiết theo Id, xóa mềm. Upload/PUT theo scope → V2.
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
            p, s, createdAtFrom, createdAtTo, null, null, null, null, originalFileNameContains, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.AttachmentListSuccess, data });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    { // Mở action — chi tiết một attachment.
        var data = await _service.GetByIdAsync(id, ct);
        return Ok(new { message = ApiMessages.AttachmentGetSuccess, data });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    { // Mở action — xóa mềm attachment.
        await _service.SoftDeleteAsync(id, DeletedBy(), ct);
        return Ok(new { message = ApiMessages.Ok });
    }
}
