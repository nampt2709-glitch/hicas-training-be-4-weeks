using System.IdentityModel.Tokens.Jwt; 
using System.Security.Claims; 
using CommentAPI; 
using Microsoft.AspNetCore.Http; 

namespace CommentAPI.Controllers; 

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
    } // Đóng GetRequiredUserId.
} // Đóng HttpContextUserId.
