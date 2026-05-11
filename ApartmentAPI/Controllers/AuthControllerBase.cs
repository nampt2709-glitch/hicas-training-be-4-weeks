using System.IdentityModel.Tokens.Jwt; // JwtRegisteredClaimNames.Sub.
using System.Security.Claims; // ClaimTypes.NameIdentifier.
using ApartmentAPI; // ApiException, ApiMessages, ApiErrorCodes.
using ApartmentAPI.Authorization; // ApiAuthorization.
using ApartmentAPI.DTOs; // SignUpRequestDto, LoginRequestDto, RefreshRequestDto.
using ApartmentAPI.Services; // IAuthenticationService.
using Microsoft.AspNetCore.Authorization; // Authorize — logout yêu cầu JWT + role.
using Microsoft.AspNetCore.Mvc; // ControllerBase.

namespace ApartmentAPI.Controllers;

// Logic chung đăng ký / đăng nhập / refresh / logout — tái sử dụng cho api/auth (legacy) và api/v{version}/auth (V1, V2).
public abstract class AuthControllerBase : ControllerBase
{
    private readonly IAuthenticationService _authenticationService;

    protected AuthControllerBase(IAuthenticationService authenticationService) =>
        _authenticationService = authenticationService;

    // POST signup — 201 + cặp token.
    protected async Task<IActionResult> ExecuteSignUpAsync(SignUpRequestDto request, CancellationToken cancellationToken)
    {
        var tokens = await _authenticationService.SignUpAsync(request, cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            new { message = ApiMessages.AuthSignUpSuccess, data = tokens });
    }

    // POST login — access + refresh.
    protected async Task<IActionResult> ExecuteLoginAsync(LoginRequestDto request, CancellationToken cancellationToken)
    {
        var tokens = await _authenticationService.LoginAsync(request, cancellationToken);
        return Ok(new { message = ApiMessages.AuthLoginSuccess, data = tokens });
    }

    // POST refresh — rotate refresh, access mới.
    protected async Task<IActionResult> ExecuteRefreshAsync(RefreshRequestDto request, CancellationToken cancellationToken)
    {
        var tokens = await _authenticationService.RefreshAsync(request, cancellationToken);
        return Ok(new { message = ApiMessages.AuthRefreshSuccess, data = tokens });
    }

    // POST logout — thu hồi refresh; cần principal hợp lệ (Admin/User).
    protected async Task<IActionResult> ExecuteLogoutAsync(CancellationToken cancellationToken)
    {
        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrEmpty(uidStr) || !Guid.TryParse(uidStr, out var userId))
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.LogoutUserUnknown,
                ApiMessages.LogoutUserUnknown);
        }

        await _authenticationService.LogoutAsync(userId, cancellationToken);
        return Ok(new { message = ApiMessages.LogoutSucceeded });
    }
}
