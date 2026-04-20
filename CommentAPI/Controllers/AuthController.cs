using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Interfaces;
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

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        var tokens = await _authenticationService.LoginAsync(request, cancellationToken);
        return Ok(tokens);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequestDto request, CancellationToken cancellationToken)
    {
        var tokens = await _authenticationService.RefreshAsync(request, cancellationToken);
        return Ok(tokens);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
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
