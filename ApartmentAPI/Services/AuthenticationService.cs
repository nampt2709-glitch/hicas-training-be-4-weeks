using System.IdentityModel.Tokens.Jwt; // JwtRegisteredClaimNames, handler.
using System.Security.Claims; // Claim, ClaimTypes.
using System.Text; // Encoding.UTF8 cho signing key.
using ApartmentAPI; // ApiException, ApiErrorCodes, ApiMessages.
using ApartmentAPI.DTOs; // SignUpRequestDto, LoginRequestDto, RefreshRequestDto, TokenResponseDto.
using ApartmentAPI.Entities; // User.
using ApartmentAPI.Interfaces; // IAuthenticationService, IAuthenticationRepository (qua ctor).
using Microsoft.AspNetCore.Http; // StatusCodes cho ApiException.
using Microsoft.IdentityModel.Tokens; // JwtSecurityToken, TokenValidationParameters.

namespace ApartmentAPI.Services;

// JWT access/refresh + security stamp (đồng bộ với CommentAPI; không ghi Serilog file).
public sealed class AuthenticationService : IAuthenticationService
{
    private const string TokenTypeClaim = "token_type"; // Phân biệt access vs refresh trong cùng issuer.
    private const string AccessTokenType = "access"; // Token mang role + unique name.
    private const string RefreshTokenType = "refresh"; // Token chỉ mang sub + stamp + jti.

    private readonly IAuthenticationRepository _authRepository; // User, password, stamp, revoke.
    private readonly IUserAppService _userAppService; // Đăng ký + gán role mặc định.
    private readonly JwtOptions _jwt; // Issuer, audience, key, TTL.

    public AuthenticationService(
        IAuthenticationRepository authRepository,
        IUserAppService userAppService,
        Microsoft.Extensions.Options.IOptions<JwtOptions> jwtOptions)
    { // Mở khối constructor.
        _authRepository = authRepository;
        _userAppService = userAppService;
        _jwt = jwtOptions.Value;
    } // Kết thúc constructor.

    public async Task<TokenResponseDto> SignUpAsync(SignUpRequestDto request, CancellationToken cancellationToken = default)
    { // Mở khối SignUpAsync.
        // BƯỚC 1 — Tạo user + role User qua UserAppService.
        var created = await _userAppService.SignUpWithDefaultUserRoleAsync(request, cancellationToken);
        // BƯỚC 2 — Nạp lại entity đầy đủ từ auth repo (đảm bảo có cho issuing token).
        var user = await _authRepository.GetByIdAsync(created.Id, cancellationToken);
        if (user is null)
        {
            throw new ApiException(
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.TokenIssueFailed,
                ApiMessages.TokenIssueFailed);
        }

        // BƯỚC 3 — Lấy roles và phát cặp JWT.
        var roles = await _authRepository.GetRoleNamesAsync(user, cancellationToken);
        return await CreateTokenPairAsync(user, roles, cancellationToken);
    } // Kết thúc SignUpAsync.

    public async Task<TokenResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    { // Mở khối LoginAsync.
        // BƯỚC 1 — Tìm user theo tên đăng nhập.
        var user = await _authRepository.GetByUserNameAsync(request.UserName, cancellationToken);
        if (user is null)
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.LoginFailed,
                ApiMessages.LoginFailed);
        }

        // BƯỚC 2 — Xác thực mật khẩu (Identity hash).
        if (!await _authRepository.ValidatePasswordAsync(user, request.Password, cancellationToken))
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.LoginFailed,
                ApiMessages.LoginFailed);
        }

        var roles = await _authRepository.GetRoleNamesAsync(user, cancellationToken);
        return await CreateTokenPairAsync(user, roles, cancellationToken);
    } // Kết thúc LoginAsync.

    public async Task<TokenResponseDto> RefreshAsync(RefreshRequestDto request, CancellationToken cancellationToken = default)
    { // Mở khối RefreshAsync.
        // BƯỚC 1 — Validate chữ ký + lifetime + kiểu token == refresh.
        var principal = ValidateRefreshTokenPrincipal(request.RefreshToken);
        if (principal is null)
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.RefreshFailed,
                ApiMessages.RefreshFailed);
        }

        // BƯỚC 2 — Sub phải parse được Guid userId.
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (sub is null || !Guid.TryParse(sub, out var userId))
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.RefreshFailed,
                ApiMessages.RefreshFailed);
        }

        // BƯỚC 3 — User còn tồn tại.
        var user = await _authRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.RefreshFailed,
                ApiMessages.RefreshFailed);
        }

        // BƯỚC 4 — Security stamp trong token khớp stamp hiện tại (đổi mật khẩu / revoke session làm hỏng refresh cũ).
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
    } // Kết thúc RefreshAsync.

    public async Task LogoutAsync(Guid userId, CancellationToken cancellationToken = default)
    { // Mở khối LogoutAsync — thu hồi phiên (nếu user còn).
        var user = await _authRepository.GetByIdAsync(userId, cancellationToken);
        if (user is not null)
            await _authRepository.RevokeSessionsAsync(user, cancellationToken);
    } // Kết thúc LogoutAsync.

    private async Task<TokenResponseDto> CreateTokenPairAsync(User user, IReadOnlyList<string> roles, CancellationToken cancellationToken)
    { // Mở khối CreateTokenPairAsync.
        // BƯỚC 1 — Lấy security stamp; bắt buộc để gắn vào cả access và refresh.
        var stamp = await _authRepository.GetSecurityStampAsync(user, cancellationToken);
        if (string.IsNullOrEmpty(stamp))
        {
            throw new ApiException(
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.TokenIssueFailed,
                ApiMessages.TokenIssueFailed);
        }

        // BƯỚC 2 — Tính thời điểm hết hạn theo cấu hình phút/ngày.
        var accessExpires = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenMinutes);
        var refreshExpires = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays);

        // BƯỚC 3 — Hai JWT: access có roles; refresh không có role claim.
        var accessToken = CreateJwt(user, roles, accessExpires, AccessTokenType, stamp);
        var refreshToken = CreateJwt(user, Array.Empty<string>(), refreshExpires, RefreshTokenType, stamp);

        return new TokenResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAtUtc = accessExpires,
            RefreshTokenExpiresAtUtc = refreshExpires,
        };
    } // Kết thúc CreateTokenPairAsync.

    private string CreateJwt(User user, IReadOnlyList<string> roles, DateTime expiresUtc, string tokenType, string securityStamp)
    { // Mở khối CreateJwt.
        // BƯỚC 1 — Khởi tạo signing HMAC-SHA256 từ khóa cấu hình.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var handler = new JwtSecurityTokenHandler();
        var now = DateTime.UtcNow;
        var jti = Guid.NewGuid().ToString(); // Id phiên token.

        // BƯỚC 2 — Claims cốt lõi: sub, loại token, jti, security stamp.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(TokenTypeClaim, tokenType),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtOptions.SecurityStampClaimType, securityStamp),
        };

        // TRƯỜNG HỢP A: Access token — thêm unique name + mọi role.
        if (tokenType == AccessTokenType)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty));
            foreach (var role in roles)
                claims.Add(new Claim(ClaimTypes.Role, role));
        }

        // BƯỚC 3 — Ký và serialise chuỗi JWT.
        var token = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, now, expiresUtc, creds);
        return handler.WriteToken(token);
    } // Kết thúc CreateJwt.

    private ClaimsPrincipal? ValidateRefreshTokenPrincipal(string refreshToken)
    { // Mở khối ValidateRefreshTokenPrincipal.
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));

        try
        { // Mở khối try validate.
            var principal = handler.ValidateToken(refreshToken, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = _jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = _jwt.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            }, out _);

            var type = principal.FindFirstValue(TokenTypeClaim);
            if (type != RefreshTokenType)
                return null; // Không phải refresh — từ chối dùng access làm refresh.

            return principal;
        }
        catch
        {
            return null; // Chữ ký hết hạn / sai — coi như null.
        }
    } // Kết thúc ValidateRefreshTokenPrincipal.
}
