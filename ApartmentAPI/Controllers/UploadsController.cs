using System.IdentityModel.Tokens.Jwt; // JwtRegisteredClaimNames — claim sub khi không dùng NameIdentifier.
using System.Security.Claims; // ClaimTypes.NameIdentifier.
using ApartmentAPI; // ApiException, ApiErrorCodes, ApiMessages.
using ApartmentAPI.Authorization; // ApiAuthorization — cùng policy role với controller đính kèm.
using ApartmentAPI.Services; // IAttachmentService — thay avatar (replace) từ uploads.
using ApartmentAPI.V2.Controllers; // AttachmentsController — liên kết 201 tới GET chi tiết (v2).
using ApartmentAPI.V1.DTOs; // UploadsAvatarUploadModel.
using ApartmentAPI.Versioning; // ApiVersionRouteValues — ép segment api/v2/attachments/{id} cho Location.
using Microsoft.AspNetCore.Authorization; // Authorize.
using Microsoft.AspNetCore.Http; // StatusCodes — 401 khi claim user thiếu/sai.
using Microsoft.AspNetCore.Mvc; // API upload (multipart).

namespace ApartmentAPI.Controllers;

// Upload đã đăng nhập — không userId trên URL; POST avatar thay mọi avatar cũ + User.AvatarUrl (giống luồng PUT).
[ApiController]
[Authorize(Roles = ApiAuthorization.AdminOrUser)]
[Route("api/uploads")]
public sealed class UploadsController : ControllerBase
{
    private readonly IAttachmentService _attachments;

    public UploadsController(IAttachmentService attachments) => _attachments = attachments;

    // Đọc Id user từ JWT (NameIdentifier hoặc sub) — khớp cấu hình Bearer NameClaimType.
    private Guid RequireCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(raw, out var id) || id == Guid.Empty)
            throw new ApiException(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthenticated, ApiMessages.Unauthenticated);
        return id;
    }

    // POST /api/uploads/avatar — avatar cho chính user đang đăng nhập (multipart, field File như route users/{id}/avatar).
    [HttpPost("avatar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(35 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 35 * 1024 * 1024)]
    public async Task<IActionResult> UploadAvatar([FromForm] UploadsAvatarUploadModel model, CancellationToken ct)
    {
        var userId = RequireCurrentUserId();
        var (data, replaced) =
            await _attachments.CreateOrReplaceAvatarForUserAsync(userId, model.File, User.Identity?.Name, ct);
        var routeValues = ApiVersionRouteValues.WithVersion(this, new { id = data.Id });
        routeValues["version"] = "2.0";
        if (replaced)
            return Ok(new { message = ApiMessages.Ok, data });
        return CreatedAtAction(
            nameof(AttachmentsController.GetById),
            "Attachments",
            routeValues,
            new { message = ApiMessages.Ok, data });
    }
}
