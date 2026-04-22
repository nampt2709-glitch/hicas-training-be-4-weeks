using System.IdentityModel.Tokens.Jwt; 
using System.Security.Claims; 
using CommentAPI; 
using Microsoft.AspNetCore.Http; 

namespace CommentAPI.Controllers; 

/// Tiện ích tập trung: lấy Guid định danh người dùng đã đăng nhập từ <see cref="ClaimsPrincipal"/>
/// (thường là User trên controller, lấy từ HttpContext.User sau khi JWT được xác thực).
/// 
/// Mục đích: cung cấp một điểm gọi duy nhất để controller biết &quot;ai đang gọi API&quot; dưới dạng Guid,
/// phục vụ các luồng tác giả / chính chủ (ví dụ cập nhật post, hồ sơ user, comment) — truyền Guid xuống service để đối chiếu chủ sở hữu.</para>
/// Lý do tồn tại: tránh lặp lại cùng một đoạn đọc claim, parse Guid và xử lý lỗi 401 ở nhiều controller;
/// khi đổi quy tắc map claim chỉ cần sửa một chỗ.
/// Luồng: client gửi Bearer token → middleware JWT + [Authorize] điền HttpContext.User

/// → action gọi GetRequiredUserId với User → nhận Guid hoặc ném ApiException 401 (middleware API chuẩn hóa thành JSON lỗi).

/// Logic: ưu tiên <see cref="ClaimTypes.NameIdentifier"/> (chuẩn ASP.NET, thường map từ sub);
/// nếu trống thì đọc <see cref="JwtRegisteredClaimNames.Sub"/>; chuỗi phải <see cref="Guid.TryParse(string, out Guid)"/> thành công,
/// ngược lại coi là chưa xác thực hợp lệ và trả lỗi Unauthenticated

// Đọc sub / NameIdentifier; ném ApiException 401 nếu thiếu hoặc Guid sai — tóm tắt nhiệm vụ lớp.
internal static class HttpContextUserId // Lớp tĩnh, không tạo instance, chứa hàm phục vụ tất cả controller.
{ // Mở phạm vi lớp tiện ích đọc định danh người dùng.
    public static Guid GetRequiredUserId(ClaimsPrincipal user) // Hàm trả về Guid user hiện tại hoặc ném 401; user là User từ HttpContext.
    { // Mở thân phương thức.
        var s = user.FindFirstValue(ClaimTypes.NameIdentifier) // Thử lấy claim chuẩn ASP.NET map từ sub.
            ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub); // Nếu thiếu, đọc claim "sub" chuẩn JWT tên ngắn.
        if (string.IsNullOrEmpty(s) || !Guid.TryParse(s, out var id)) // Nếu không chuỗi hoặc parse Guid thất bại thì thiếu danh tính.
        { // Nhánh lỗi thiếu claim hợp lệ.
            throw new ApiException( // Dùng ngoại lệ thống nhất API, middleware chuyển thành JSON lỗi.
                StatusCodes.Status401Unauthorized, // Mã HTTP 401, client phải gửi lại access token hợp lệ.
                ApiErrorCodes.Unauthenticated, // Mã lỗi nội bộ, client có thể map i18n.
                ApiMessages.Unauthenticated); // Thông điệp mô tả từ ApiMessages.
        } // Kết nhánh throw.

        return id; // Trả về Guid user đã xác thực dùng cho kiểm tra tác giả, v.v.
    } 
} 
