using System.IdentityModel.Tokens.Jwt; // JwtRegisteredClaimNames — fallback sub.
using System.Security.Claims; // ClaimTypes.NameIdentifier.
using ApartmentAPI; // ApiException, ApiErrorCodes, ApiMessages.
using ApartmentAPI.Authorization; // ApiAuthorization.
using ApartmentAPI.Services; // IAttachmentService.
using ApartmentAPI.V1.DTOs; // UploadsAvatarUploadModel.
using ApartmentAPI.Versioning; // ApiVersionRouteValues.WithVersion.
using Asp.Versioning; // [ApiVersion].
using Microsoft.AspNetCore.Authorization; // Authorize.
using Microsoft.AspNetCore.Http; // StatusCodes.
using Microsoft.AspNetCore.Mvc; // Multipart avatar cho user hiện tại.

namespace ApartmentAPI.V2.Controllers;

// V2 — POST avatar của user hiện tại: thay mọi avatar cũ + User.AvatarUrl; thiếu file → ApiMessages (validator).
[ApiController]
[ApiVersion("2.0")]
[Authorize(Roles = ApiAuthorization.AdminOrUser)]
[Route("api/v{version:apiVersion}/uploads")]
public sealed class UploadsController : ControllerBase
{
    private readonly IAttachmentService _attachments;

    public UploadsController(IAttachmentService attachments) => _attachments = attachments;

    // Lấy Guid user từ principal — NameIdentifier hoặc sub.
    private Guid RequireCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(raw, out var id) || id == Guid.Empty)
            throw new ApiException(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthenticated, ApiMessages.Unauthenticated);
        return id;
    }

    // POST .../api/v2/uploads/avatar — multipart; không truyền userId trên URL.
    [HttpPost("avatar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(35 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 35 * 1024 * 1024)]
    public async Task<IActionResult> UploadAvatar([FromForm] UploadsAvatarUploadModel model, CancellationToken ct)
    {
        var userId = RequireCurrentUserId();
        var (data, replaced) =
            await _attachments.CreateOrReplaceAvatarForUserAsync(userId, model.File, User.Identity?.Name, ct);
        if (replaced)
            return Ok(new { message = ApiMessages.Ok, data });
        return CreatedAtAction(
            nameof(AttachmentsController.GetById),
            "Attachments",
            ApiVersionRouteValues.WithVersion(this, new { id = data.Id }),
            new { message = ApiMessages.Ok, data });
    }
}
