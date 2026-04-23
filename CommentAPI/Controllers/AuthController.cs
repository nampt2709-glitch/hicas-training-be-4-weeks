using System.IdentityModel.Tokens.Jwt; 
using System.Security.Claims; 
using CommentAPI; 
using CommentAPI.DTOs; 
using CommentAPI.Interfaces; 
using Microsoft.AspNetCore.Authorization; 
using Microsoft.AspNetCore.Mvc; 

namespace CommentAPI.Controllers; 

[ApiController] // Kích hoạt hành vi và validation mặc định cho Web API.
[Route("api/auth")] // Cơ sở đường dẫn cho mọi endpoint xác thực.
public class AuthController : ControllerBase // Controller không có view, chỉ JSON.
{
    private readonly IAuthenticationService _authenticationService; // Dịch vụ nghiệp vụ auth được inject.

    public AuthController(IAuthenticationService authenticationService) // Tiêm phụ thuộc qua constructor.
    {
        _authenticationService = authenticationService; // Gán instance để dùng trong action.
    }

    [AllowAnonymous] // Đăng ký không cần JWT; middleware whitelist /api/auth/signup.
    [HttpPost("signup")] // POST tạo tài khoản + trả cặp token (giống login sau khi tạo xong).
    public async Task<IActionResult> SignUp([FromBody] SignUpRequestDto request, CancellationToken cancellationToken) // Body: Name, UserName, Password, Email?.
    {
        var tokens = await _authenticationService.SignUpAsync(request, cancellationToken); // Identity Create + role User + JWT.
        return StatusCode(
            StatusCodes.Status201Created,
            new { message = ApiMessages.AuthSignUpSuccess, data = tokens }); // 201 + message nhất quán.
    }

    [AllowAnonymous] // Không yêu cầu Bearer token cho đăng nhập.
    [HttpPost("login")] // POST tạo phiên: nhận thông tin đăng nhập, trả cặp token.
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken) // Body JSON map vào DTO; hủy theo token client.
    {
        var tokens = await _authenticationService.LoginAsync(request, cancellationToken); // Gọi service: kiểm tra credential, phát token.
        return Ok(new { message = ApiMessages.AuthLoginSuccess, data = tokens }); // 200 + message + data.
    }

    [AllowAnonymous] // Làm mới token không cần access còn hiệu lực (chỉ refresh hợp lệ).
    [HttpPost("refresh")] // POST đổi refresh token lấy bộ token mới.
    public async Task<IActionResult> Refresh([FromBody] RefreshRequestDto request, CancellationToken cancellationToken) // Body chứa refresh token.
    {
        var tokens = await _authenticationService.RefreshAsync(request, cancellationToken); // Xác thực refresh, xoay vòng token.
        return Ok(new { message = ApiMessages.AuthRefreshSuccess, data = tokens }); // 200 + message + data.
    }

    [Authorize] // Bắt buộc JWT access hợp lệ (middleware + bearer).
    [HttpPost("logout")] // POST hủy phiên phía server (vô hiệu hóa refresh nếu có).
    public async Task<IActionResult> Logout(CancellationToken cancellationToken) // Không body; user lấy từ claims.
    {
        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier) // Ưu tiên claim NameIdentifier (map thường từ sub).
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub); // Dự phòng: đọc trực tiếp claim sub chuẩn JWT.
        if (string.IsNullOrEmpty(uidStr) || !Guid.TryParse(uidStr, out var userId)) // Thiếu hoặc không parse được Guid user.
        {
            throw new ApiException( // Lỗi có cấu trúc: handler trả JSON 401.
                StatusCodes.Status401Unauthorized, // HTTP 401 — không xác định được user để logout.
                ApiErrorCodes.LogoutUserUnknown, // Mã lỗi ứng dụng cho client/i18n.
                ApiMessages.LogoutUserUnknown); // Thông điệp người dùng cuối.
        }

        await _authenticationService.LogoutAsync(userId, cancellationToken); // Thu hồi session/refresh theo user id.
        return Ok(new { message = ApiMessages.LogoutSucceeded }); // 200 xác nhận logout thành công.
    }
}
