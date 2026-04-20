using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Entities;
using CommentAPI.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CommentAPI.Services;

public class AuthenticationService : IAuthenticationService
{
    private const string TokenTypeClaim = "token_type";
    private const string AccessTokenType = "access";
    private const string RefreshTokenType = "refresh";

    private readonly IAuthenticationRepository _authRepository;
    private readonly JwtOptions _jwt;

    public AuthenticationService(IAuthenticationRepository authRepository, IOptions<JwtOptions> jwtOptions)
    {
        _authRepository = authRepository;
        _jwt = jwtOptions.Value;
    }

    public async Task<TokenResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await _authRepository.GetByUserNameAsync(request.UserName, cancellationToken);
        if (user is null)
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.LoginFailed,
                ApiMessages.LoginFailed);
        }

        if (!await _authRepository.ValidatePasswordAsync(user, request.Password, cancellationToken))
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.LoginFailed,
                ApiMessages.LoginFailed);
        }

        var roles = await _authRepository.GetRoleNamesAsync(user, cancellationToken);
        return await CreateTokenPairAsync(user, roles, cancellationToken);
    }

    public async Task<TokenResponseDto> RefreshAsync(RefreshRequestDto request, CancellationToken cancellationToken = default)
    {
        var principal = ValidateRefreshTokenPrincipal(request.RefreshToken);
        if (principal is null)
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.RefreshFailed,
                ApiMessages.RefreshFailed);
        }

        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (sub is null || !Guid.TryParse(sub, out var userId))
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.RefreshFailed,
                ApiMessages.RefreshFailed);
        }

        var user = await _authRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.RefreshFailed,
                ApiMessages.RefreshFailed);
        }

        var stampInToken = principal.FindFirstValue(JwtOptions.SecurityStampClaimType);
        var currentStamp = await _authRepository.GetSecurityStampAsync(user, cancellationToken);
        if (string.IsNullOrEmpty(stampInToken) || stampInToken != currentStamp)
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.RefreshFailed,
                ApiMessages.RefreshFailed);
        }

        var roles = await _authRepository.GetRoleNamesAsync(user, cancellationToken);
        return await CreateTokenPairAsync(user, roles, cancellationToken);
    }

    public async Task LogoutAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _authRepository.GetByIdAsync(userId, cancellationToken);
        if (user is not null)
        {
            await _authRepository.RevokeSessionsAsync(user, cancellationToken);
        }
    }

    private async Task<TokenResponseDto> CreateTokenPairAsync(User user, IReadOnlyList<string> roles, CancellationToken cancellationToken)
    {
        var stamp = await _authRepository.GetSecurityStampAsync(user, cancellationToken);
        if (string.IsNullOrEmpty(stamp))
        {
            throw new ApiException(
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.TokenIssueFailed,
                ApiMessages.TokenIssueFailed);
        }

        var accessExpires = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenMinutes);
        var refreshExpires = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays);
        var accessToken = CreateJwt(user, roles, accessExpires, AccessTokenType, stamp);
        var refreshToken = CreateJwt(user, Array.Empty<string>(), refreshExpires, RefreshTokenType, stamp);

        return new TokenResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAtUtc = accessExpires,
            RefreshTokenExpiresAtUtc = refreshExpires
        };
    }

    private string CreateJwt(User user, IReadOnlyList<string> roles, DateTime expiresUtc, string tokenType, string securityStamp)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(TokenTypeClaim, tokenType),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtOptions.SecurityStampClaimType, securityStamp)
        };

        if (tokenType == AccessTokenType)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty));
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresUtc,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private ClaimsPrincipal? ValidateRefreshTokenPrincipal(string refreshToken)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        try
        {
            var principal = handler.ValidateToken(refreshToken, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = _jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = _jwt.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);

            var type = principal.FindFirstValue(TokenTypeClaim);
            if (type != RefreshTokenType)
            {
                return null;
            }

            return principal;
        }
        catch
        {
            return null;
        }
    }
}
