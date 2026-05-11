using ApartmentAPI.Authorization; // ApiAuthorization.
using ApartmentAPI.DTOs; // SignUpRequestDto, LoginRequestDto, RefreshRequestDto.
using ApartmentAPI.Services; // IAuthenticationService.
using Asp.Versioning; // ApiVersionNeutral — không thuộc doc V1/V2.
using Microsoft.AspNetCore.Authorization; // AllowAnonymous, Authorize.
using Microsoft.AspNetCore.Mvc; // ApiController, ApiExplorerSettings.

namespace ApartmentAPI.Controllers;

// Tương thích client gọi /api/auth không có segment version; không đưa vào Swagger để tránh trùng với nhóm Auth đã version (ảnh chụp Swagger).
[AllowAnonymous]
[ApiController]
[ApiVersionNeutral]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/auth")]
public sealed class UnversionedAuthController : AuthControllerBase
{
    public UnversionedAuthController(IAuthenticationService authenticationService)
        : base(authenticationService)
    {
    }

    [AllowAnonymous]
    [HttpPost("signup")]
    public Task<IActionResult> SignUp([FromBody] SignUpRequestDto request, CancellationToken cancellationToken) =>
        ExecuteSignUpAsync(request, cancellationToken);

    [AllowAnonymous]
    [HttpPost("login")]
    public Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken) =>
        ExecuteLoginAsync(request, cancellationToken);

    [AllowAnonymous]
    [HttpPost("refresh")]
    public Task<IActionResult> Refresh([FromBody] RefreshRequestDto request, CancellationToken cancellationToken) =>
        ExecuteRefreshAsync(request, cancellationToken);

    [Authorize(Roles = ApiAuthorization.AdminOrUser)]
    [HttpPost("logout")]
    public Task<IActionResult> Logout(CancellationToken cancellationToken) =>
        ExecuteLogoutAsync(cancellationToken);
}
