using System.IdentityModel.Tokens.Jwt; // JwtRegisteredClaimNames.Sub làm fallback khi không có ClaimTypes.NameIdentifier.
using System.Security.Claims; // ClaimTypes để đọc user Id từ principal sau Bearer.
using ApartmentAPI; // ApiMessages, ApiErrorCodes, ApiException.
using ApartmentAPI.DTOs; // SignUpRequestDto, LoginRequestDto, RefreshRequestDto.
using ApartmentAPI.Authorization; // ApiAuthorization — chuỗi role Admin/User thống nhất.
using ApartmentAPI.Services; // IAuthenticationService: SignUpAsync, LoginAsync, RefreshAsync, LogoutAsync.
using Microsoft.AspNetCore.Authorization; // [AllowAnonymous], [Authorize].
using Microsoft.AspNetCore.Mvc; // ControllerBase, IActionResult, FromBody.

namespace ApartmentAPI.Controllers;

// Api xác thực độc lập phiên bản URL: signup / login / refresh (anonymous), logout (JWT có role Admin hoặc User).
// Đặt ngoài v1/v2 vì một đường dẫn cố định /api/auth cho toàn tenant.
[AllowAnonymous] // Mặc định cho controller; action logout ghi đè bằng [Authorize] yêu cầu JWT hợp lệ + role.
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authenticationService; // Dịch vụ đăng ký, JWT, revoke refresh.

    public AuthController(IAuthenticationService authenticationService) // Tiêm IAuthenticationService.
    {
        _authenticationService = authenticationService; // Lưu tham chiếu dùng trong mọi action.
    }

    [AllowAnonymous]
    [HttpPost("signup")]
    public async Task<IActionResult> SignUp([FromBody] SignUpRequestDto request, CancellationToken cancellationToken)
    {
        // BƯỚC 1 — Tạo user Identity + vai trò mặc định và cặp token (access + refresh).
        var tokens = await _authenticationService.SignUpAsync(request, cancellationToken);
        // BƯỚC 2 — 201 Created với envelope message + data (token pair).
        return StatusCode(
            StatusCodes.Status201Created,
            new { message = ApiMessages.AuthSignUpSuccess, data = tokens });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        // BƯỚC 1 — Xác thực credential và phát JWT + refresh hash lưu DB.
        var tokens = await _authenticationService.LoginAsync(request, cancellationToken);
        return Ok(new { message = ApiMessages.AuthLoginSuccess, data = tokens });
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequestDto request, CancellationToken cancellationToken)
    {
        // BƯỚC 1 — Rotate refresh token và trả access mới khi refresh còn hạn và chưa thu hồi.
        var tokens = await _authenticationService.RefreshAsync(request, cancellationToken);
        return Ok(new { message = ApiMessages.AuthRefreshSuccess, data = tokens });
    }

    // Đăng xuất: chỉ tài khoản đã đăng nhập (Admin hoặc User); trùng khớp với seed role sau signup.
    [Authorize(Roles = ApiAuthorization.AdminOrUser)]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        // BƯỚC 1 — Trích Guid user từ claim chuẩn (hoặc sub JWT).
        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        // BƯỚC 2 — Nếu không parse được → 401 đồng bộ với client (token hỏng/thiếu sub).
        if (string.IsNullOrEmpty(uidStr) || !Guid.TryParse(uidStr, out var userId))
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.LogoutUserUnknown,
                ApiMessages.LogoutUserUnknown);
        }

        // BƯỚC 3 — Thu hồi refresh của user trong service (invalidate phiên hiện tại).
        await _authenticationService.LogoutAsync(userId, cancellationToken);
        return Ok(new { message = ApiMessages.LogoutSucceeded });
    }
}
