using System.IdentityModel.Tokens.Jwt; // Sub claim khi Logout.
using System.Security.Claims; // NameIdentifier.
using Asp.Versioning; // ApiVersion 2.0.
using CommentAPI; // ApiException, mã lỗi, thông điệp.
using CommentAPI.DTOs; // Body auth.
using CommentAPI.Interfaces; // IAuthenticationService.
using Microsoft.AspNetCore.Authorization; // AllowAnonymous, Authorize.
using Microsoft.AspNetCore.Http; // StatusCodes.
using Microsoft.AspNetCore.Mvc; // ControllerBase.

namespace CommentAPI.V2.Controllers;

// =============================================================================
// File V2/AuthController.cs: cùng hành vi V1 — URL /api/v2.0/auth/... (segment version).
// =============================================================================

// Bản 2.0: cùng hành vi 1.0; endpoint /api/v2/auth/... (versioning URL segment).
[ApiController]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authenticationService; // Cùng dịch vụ với V1.

    public AuthController(IAuthenticationService authenticationService) =>
        _authenticationService = authenticationService;

    [AllowAnonymous]
    [HttpPost("signup")]
    public async Task<IActionResult> SignUp([FromBody] SignUpRequestDto request, CancellationToken cancellationToken)
    { // Mở khối SignUp V2.
        // BƯỚC 1 — Sign up + token (delegation).
        var tokens = await _authenticationService.SignUpAsync(request, cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            new { message = ApiMessages.AuthSignUpSuccess, data = tokens });
    } // Kết thúc SignUp.

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    { // Mở khối Login V2.
        // BƯỚC 1 — Login + token.
        var tokens = await _authenticationService.LoginAsync(request, cancellationToken);
        return Ok(new { message = ApiMessages.AuthLoginSuccess, data = tokens });
    } // Kết thúc Login.

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequestDto request, CancellationToken cancellationToken)
    { // Mở khối Refresh V2.
        // BƯỚC 1 — Refresh rotation.
        var tokens = await _authenticationService.RefreshAsync(request, cancellationToken);
        return Ok(new { message = ApiMessages.AuthRefreshSuccess, data = tokens });
    } // Kết thúc Refresh.

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    { // Mở khối Logout V2.
        // BƯỚC 1 — Parse user id từ access.
        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrEmpty(uidStr) || !Guid.TryParse(uidStr, out var userId))
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.LogoutUserUnknown,
                ApiMessages.LogoutUserUnknown);
        }

        // BƯỚC 2 — Revoke sessions qua UpdateSecurityStamp.
        await _authenticationService.LogoutAsync(userId, cancellationToken);
        return Ok(new { message = ApiMessages.LogoutSucceeded });
    } // Kết thúc Logout.
} // Kết thúc lớp AuthController V2.
