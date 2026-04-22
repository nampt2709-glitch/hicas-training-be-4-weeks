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

// Triển khai IAuthenticationService: tách khỏi controller, dùng repository + JwtOptions.
public class AuthenticationService : IAuthenticationService
{
    // Tên custom claim: loại token (access vs refresh).
    private const string TokenTypeClaim = "token_type";
    // Giá trị claim: access token dùng gọi API (kèm role).
    private const string AccessTokenType = "access";
    // Giá trị claim: refresh token chỉ dùng endpoint refresh, không cần role.
    private const string RefreshTokenType = "refresh";

    // Truy cập user, mật khẩu, role, security stamp, revoke.
    private readonly IAuthenticationRepository _authRepository;
    // Cấu hình issuer, audience, key, thời gian sống, đọc từ IOptions.
    private readonly JwtOptions _jwt;

    // Inject repository + options (scoped + singleton/IOptions tùy cấu hình).
    public AuthenticationService(IAuthenticationRepository authRepository, IOptions<JwtOptions> jwtOptions)
    {
        _authRepository = authRepository; // Lưu tham chiếu repository
        _jwt = jwtOptions.Value; // Snapshot cấu hình JWT
    }

    // Đăng nhập: tìm user, kiểm tra mật khẩu, lấy role, tạo cặp token.
    public async Task<TokenResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await _authRepository.GetByUserNameAsync(request.UserName, cancellationToken); // Identity FindByName
        if (user is null) // Không lộ tồn tại: cùng thông điệp với mật khẩu sai
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized, // 401: credentials sai
                ApiErrorCodes.LoginFailed, // Mã ổn định
                ApiMessages.LoginFailed); // Chuỗi cho client
        }

        if (!await _authRepository.ValidatePasswordAsync(user, request.Password, cancellationToken)) // CheckPasswordAsync
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.LoginFailed,
                ApiMessages.LoginFailed);
        }

        var roles = await _authRepository.GetRoleNamesAsync(user, cancellationToken); // AspNetUserRoles
        return await CreateTokenPairAsync(user, roles, cancellationToken); // Phát cả access lẫn refresh
    }

    // Làm mới: validate JWT refresh, đối chiếu security stamp, phát cặp mới.
    public async Task<TokenResponseDto> RefreshAsync(RefreshRequestDto request, CancellationToken cancellationToken = default)
    {
        var principal = ValidateRefreshTokenPrincipal(request.RefreshToken); // Ký, issuer, aud, type=refresh
        if (principal is null) // Chữ ký sai, hết hạn, sai loại
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.RefreshFailed,
                ApiMessages.RefreshFailed);
        }

        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub); // User id
        if (sub is null || !Guid.TryParse(sub, out var userId)) // Bắt buộc Guid
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.RefreshFailed,
                ApiMessages.RefreshFailed);
        }

        var user = await _authRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null) // User đã xoá
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.RefreshFailed,
                ApiMessages.RefreshFailed);
        }

        var stampInToken = principal.FindFirstValue(JwtOptions.SecurityStampClaimType); // Stamp trong refresh
        var currentStamp = await _authRepository.GetSecurityStampAsync(user, cancellationToken); // Stamp hiện tại DB
        if (string.IsNullOrEmpty(stampInToken) || stampInToken != currentStamp) // Đổi mk / revoke sau khi phát refresh
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.RefreshFailed,
                ApiMessages.RefreshFailed);
        }

        var roles = await _authRepository.GetRoleNamesAsync(user, cancellationToken);
        return await CreateTokenPairAsync(user, roles, cancellationToken); // Mỗi lần refresh: cả hai token mới
    }

    // Đăng xuất: tăng security stamp để mọi token cũ (OnTokenValidated) thất bại.
    public async Task LogoutAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _authRepository.GetByIdAsync(userId, cancellationToken);
        if (user is not null) // Id hợp lệ: revoke
        {
            await _authRepository.RevokeSessionsAsync(user, cancellationToken); // UpdateSecurityStampAsync
        } // Nếu user null, im lặng (idempotent)
    }

    // Tạo access + refresh: cùng stamp; access có role, refresh không role.
    private async Task<TokenResponseDto> CreateTokenPairAsync(User user, IReadOnlyList<string> roles, CancellationToken cancellationToken)
    {
        var stamp = await _authRepository.GetSecurityStampAsync(user, cancellationToken); // Bắt buộc cho claim
        if (string.IsNullOrEmpty(stamp)) // Trạng thái Identity bất thường
        {
            throw new ApiException(
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.TokenIssueFailed,
                ApiMessages.TokenIssueFailed);
        }

        var accessExpires = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenMinutes); // Hết hạn access
        var refreshExpires = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays); // Hết hạn refresh dài hơn
        var accessToken = CreateJwt(user, roles, accessExpires, AccessTokenType, stamp); // Kèm role
        var refreshToken = CreateJwt(user, Array.Empty<string>(), refreshExpires, RefreshTokenType, stamp); // Chỉ sub + type + stamp

        return new TokenResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAtUtc = accessExpires,
            RefreshTokenExpiresAtUtc = refreshExpires
        };
    }

    // Tạo chuỗi JWT: claims gồm sub, token_type, jti, sec_stamp; access thêm tên + role.
    // Dùng ctor JwtSecurityToken(issuer, audience, claims, nbf, exp, creds) để mọi claim (kể cả token_type, sec_stamp) nằm trong payload JSON; CreateToken(descriptor) có thể không ghi đủ claim tùy chỉnh khi ReadJwtToken.
    private string CreateJwt(User user, IReadOnlyList<string> roles, DateTime expiresUtc, string tokenType, string securityStamp)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)); // Khóa từ cấu hình
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256); // Ký HMAC
        var handler = new JwtSecurityTokenHandler();
        var now = DateTime.UtcNow;
        var jti = Guid.NewGuid().ToString(); // Mỗi token một id (có thể dùng blacklist sau)

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()), // Định danh chuẩn OIDC
            new(TokenTypeClaim, tokenType), // Tách access vs refresh
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtOptions.SecurityStampClaimType, securityStamp) // So khớp GetSecurityStamp / revoke
        };

        if (tokenType == AccessTokenType) // Chỉ access: thêm tên user và role cho [Authorize]
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty));
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var token = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, now, expiresUtc, creds);
        return handler.WriteToken(token);
    }

    // Parse và validate chữ ký refresh; bắt buộc claim type = refresh; lỗi bất kỳ → null.
    private ClaimsPrincipal? ValidateRefreshTokenPrincipal(string refreshToken)
    {
        // MapInboundClaims mặc định true (IdentityModel 8) đổi "sub" → NameIdentifier; giữ tên JWT gốc để FindFirstValue(Sub) và claim tùy chỉnh khớp khi refresh.
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        try
        {
            var principal = handler.ValidateToken(refreshToken, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, // Cùng khóa với tạo
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = _jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = _jwt.Audience,
                ValidateLifetime = true, // Hết hạn thì throw, catch bên dưới
                ClockSkew = TimeSpan.Zero
            }, out _);

            var type = principal.FindFirstValue(TokenTypeClaim);
            if (type != RefreshTokenType) // Từ chối dùng access thay refresh
            {
                return null;
            }

            return principal;
        }
        catch // Bất kỳ lỗi validate: coi token không dùng được
        {
            return null;
        }
    }
}
