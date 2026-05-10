using System.IdentityModel.Tokens.Jwt; // JwtRegisteredClaimNames.Sub khi không có NameIdentifier.
using System.Security.Claims; // ClaimsPrincipal.FindFirstValue, ClaimTypes.
using CommentAPI; // ApiException, ApiErrorCodes, ApiMessages.
using Microsoft.AspNetCore.Http; // StatusCodes.Status401Unauthorized.

namespace CommentAPI.Controllers;

/// <summary>
/// Tiện ích tập trung: lấy <see cref="Guid"/> định danh người đã đăng nhập từ <see cref="ClaimsPrincipal"/>
/// (thường là <c>User</c> trên controller, lấy từ HttpContext.User sau khi JWT xác thực).
/// Mục đích: một điểm gọi để controller biết &quot;ai đang gọi API&quot; — phục vụ luồng tác giả / chánh chủ;
/// đổi quy tắc đọc claim chỉ sửa một chỗ.
/// Luồng: Bearer token → JWT + [Authorize] → action gọi <see cref="GetRequiredUserId"/> → Guid hoặc ApiException 401.
/// Ưu tiên <see cref="ClaimTypes.NameIdentifier"/>; nếu trống thì <see cref="JwtRegisteredClaimNames.Sub"/>.
/// </summary>

// Đọc sub / NameIdentifier; ném ApiException 401 nếu thiếu hoặc Guid không parse được.
internal static class HttpContextUserId // Lớp tĩnh, không khởi tạo instance.
{ // Mở phạm vi lớp.
    /// <summary>
    /// Trả Guid user đã xác thực; ném <see cref="ApiException"/> 401 Unauthenticated khi không đọc được id hợp lệ.
    /// </summary>
    public static Guid GetRequiredUserId(ClaimsPrincipal user) // user = HttpContext.User sau middleware JWT.
    { // Mở GetRequiredUserId.
        // BƯỚC 1 — Đọc claim định danh: ASP.NET NameIdentifier hoặc claim JWT &quot;sub&quot;.
        var s = user.FindFirstValue(ClaimTypes.NameIdentifier) // Chuẩn ASP.NET map từ subject.
            ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub); // Dự phòng khi chỉ có sub JWT.
        // BƯỚC 2 — Thiếu chuỗi hoặc không parse Guid ⇒ coi là chưa đăng nhập hợp lệ.
        if (string.IsNullOrEmpty(s) || !Guid.TryParse(s, out var id)) // Kiểm tra danh tính.
        { // Nhánh lỗi.
            throw new ApiException(
                StatusCodes.Status401Unauthorized, // HTTP 401.
                ApiErrorCodes.Unauthenticated, // Mã ổn định.
                ApiMessages.Unauthenticated); // Thông điệp thân thiện client.
        } // Kết nhánh throw.

        // BƯỚC 3 — Trả Guid dùng cho kiểm tra tác giả, v.v.
        return id;
    } // Kết thúc GetRequiredUserId.
} // Kết thúc lớp HttpContextUserId.
