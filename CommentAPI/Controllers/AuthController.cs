using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Interfaces;
using CommentAPI.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CommentAPI.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authenticationService;

    public AuthController(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    /// <summary>Đăng nhập; trả về access và refresh token trong body.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        var tokens = await _authenticationService.LoginAsync(request, cancellationToken);
        if (tokens is null)
        {
            var cid = CorrelationMiddleware.GetCorrelationId(HttpContext);
            Response.Headers.Append(CorrelationMiddleware.HeaderName, cid);
            CorrelationMiddleware.AppendErrorSourceHeader(HttpContext,
                $"{nameof(AuthController)}.{nameof(Login)} (login rejected: invalid credentials or user missing)");
            return Unauthorized(new
            {
                code = ApiErrorCodes.LoginFailed,
                type = nameof(AuthController.Login),
                message = ApiMessages.LoginFailed
            });
        }

        return Ok(tokens);
    }

    /// <summary>Làm mới cặp token bằng refresh token hợp lệ.</summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequestDto request, CancellationToken cancellationToken)
    {
        var tokens = await _authenticationService.RefreshAsync(request, cancellationToken);
        if (tokens is null)
        {
            var cid = CorrelationMiddleware.GetCorrelationId(HttpContext);
            Response.Headers.Append(CorrelationMiddleware.HeaderName, cid);
            CorrelationMiddleware.AppendErrorSourceHeader(HttpContext,
                $"{nameof(AuthController)}.{nameof(Refresh)} (refresh rejected: invalid token, user, or security stamp)");
            return Unauthorized(new
            {
                code = ApiErrorCodes.RefreshFailed,
                type = nameof(AuthController.Refresh),
                message = ApiMessages.RefreshFailed
            });
        }

        return Ok(tokens);
    }

    /// <summary>Đăng xuất phía server: vô hiệu hóa access/refresh đã cấp (cập nhật security stamp).</summary>
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var uidStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrEmpty(uidStr) || !Guid.TryParse(uidStr, out var userId))
        {
            var cid1 = CorrelationMiddleware.GetCorrelationId(HttpContext);
            Response.Headers.Append(CorrelationMiddleware.HeaderName, cid1);
            CorrelationMiddleware.AppendErrorSourceHeader(HttpContext,
                $"{nameof(AuthController)}.{nameof(Logout)} (cannot resolve user id from claims)");
            return Unauthorized(new
            {
                code = ApiErrorCodes.LogoutUserUnknown,
                type = nameof(AuthController.Logout),
                message = ApiMessages.LogoutUserUnknown
            });
        }

        await _authenticationService.LogoutAsync(userId, cancellationToken);
        var cid = CorrelationMiddleware.GetCorrelationId(HttpContext);
        Response.Headers.Append(CorrelationMiddleware.HeaderName, cid);
        return Ok(new { message = ApiMessages.LogoutSucceeded });
    }
}
