using System.IdentityModel.Tokens.Jwt; // JwtRegisteredClaimNames.Sub khi đọc claim logout.
using System.Security.Claims; // ClaimTypes.NameIdentifier.
using Asp.Versioning; // ApiVersion("1.0"), route v{version:apiVersion}.
using CommentAPI; // ApiException, ApiErrorCodes, ApiMessages, StatusCodes.
using CommentAPI.DTOs; // SignUpRequestDto, LoginRequestDto, RefreshRequestDto.
using CommentAPI.Interfaces; // IAuthenticationService.
using Microsoft.AspNetCore.Authorization; // AllowAnonymous, Authorize.
using Microsoft.AspNetCore.Http; // StatusCodes.
using Microsoft.AspNetCore.Mvc; // ApiController, IActionResult, FromBody.

namespace CommentAPI.V1.Controllers;

// =============================================================================
// File V1/AuthController.cs: đăng ký, đăng nhập, refresh (ẩn danh); logout (Bearer access).
// =============================================================================

// Xác thực JWT: đăng ký, đăng nhập, refresh, logout — route có segment version (giống ApartmentAPI).
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authenticationService; // Phát/validate token + logout stamp.

    public AuthController(IAuthenticationService authenticationService) =>
        _authenticationService = authenticationService;

    [AllowAnonymous]
    [HttpPost("signup")]
    public async Task<IActionResult> SignUp([FromBody] SignUpRequestDto request, CancellationToken cancellationToken)
    { // Mở khối SignUp.
        // BƯỚC 1 — Delegate AuthenticationService: tạo user + phát access/refresh.
        var tokens = await _authenticationService.SignUpAsync(request, cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            new { message = ApiMessages.AuthSignUpSuccess, data = tokens });
    } // Kết thúc SignUp.

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    { // Mở khối Login.
        // BƯỚC 1 — Kiểm tra mật khẩu + phát token.
        var tokens = await _authenticationService.LoginAsync(request, cancellationToken);
        return Ok(new { message = ApiMessages.AuthLoginSuccess, data = tokens });
    } // Kết thúc Login.

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequestDto request, CancellationToken cancellationToken)
    { // Mở khối Refresh.
        // BƯỚC 1 — Validate refresh + rotation cặp token.
        var tokens = await _authenticationService.RefreshAsync(request, cancellationToken);
        return Ok(new { message = ApiMessages.AuthRefreshSuccess, data = tokens });
    } // Kết thúc Refresh.

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    { // Mở khối Logout.
        // BƯỚC 1 — Đọc user id từ access JWT; không có → ApiException 401 LogoutUserUnknown.
        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrEmpty(uidStr) || !Guid.TryParse(uidStr, out var userId))
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.LogoutUserUnknown,
                ApiMessages.LogoutUserUnknown);
        }

        // BƯỚC 2 — Xoay security stamp (vô hiệu mọi refresh cũ).
        await _authenticationService.LogoutAsync(userId, cancellationToken);
        return Ok(new { message = ApiMessages.LogoutSucceeded });
    } // Kết thúc Logout.
} // Kết thúc lớp AuthController V1.
