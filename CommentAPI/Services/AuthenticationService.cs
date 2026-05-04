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

// Service xác thực: đăng ký, đăng nhập, refresh, logout — phát JWT access/refresh và kiểm tra security stamp.
public class AuthenticationService : IAuthenticationService
{
    #region Hằng & trường & hàm tạo — AuthController

    // Tên claim tùy chỉnh: phân biệt access token vs refresh token trong cùng một issuer/audience.
    private const string TokenTypeClaim = "token_type";

    // Giá trị claim khi token dùng để gọi API có [Authorize] — kèm role.
    private const string AccessTokenType = "access";

    // Giá trị claim khi token chỉ dùng cho POST /api/auth/refresh — không cần role trong payload.
    private const string RefreshTokenType = "refresh";

    // Repository bọc UserManager — tìm user, check password, roles, stamp, revoke.
    private readonly IAuthenticationRepository _authRepository;

    // Tạo user Identity + role User mặc định cho luồng signup (tách reuse logic admin).
    private readonly IUserService _userService;

    // Snapshot JwtOptions (issuer, audience, signing key, TTL access/refresh).
    private readonly JwtOptions _jwt;

    // BƯỚC 1: Tiêm repository + user service + IOptions JwtOptions.
    public AuthenticationService(
        IAuthenticationRepository authRepository,
        IUserService userService,
        IOptions<JwtOptions> jwtOptions)
    {
        _authRepository = authRepository; // Lưu để gọi Identity.
        _userService = userService; // Lưu để CreateAsync sau signup.
        _jwt = jwtOptions.Value; // .Value đọc cấu hình đã bind từ appsettings.
    }

    #endregion

    #region Route Functions

    // [1] POST /api/auth/signup — tạo user qua UserService rồi phát cặp token.
    public async Task<TokenResponseDto> SignUpAsync(SignUpRequestDto request, CancellationToken cancellationToken = default)
    {
        // BƯỚC 1: Delegate tạo user (Identity + hash password + role User) — ném ApiException nếu trùng username / lỗi Identity.
        var created = await _userService.CreateAsync(new CreateUserDto
        {
            Name = request.Name,
            UserName = request.UserName,
            Password = request.Password,
            Email = request.Email,
        });

        // BƯỚC 2: Nạp lại entity User đầy đủ từ store theo Id vừa tạo — cần cho GetRoles và stamp.
        var user = await _authRepository.GetByIdAsync(created.Id, cancellationToken);

        // TRƯỜNG HỢP BẤT THƯỜNG: Create thành công nhưng FindById không thấy — trạng thái không nhất quán.
        if (user is null)
        {
            throw new ApiException(
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.TokenIssueFailed,
                ApiMessages.TokenIssueFailed);
        }

        // BƯỚC 3: Lấy role names và phát access + refresh.
        var roles = await _authRepository.GetRoleNamesAsync(user, cancellationToken);
        return await CreateTokenPairAsync(user, roles, cancellationToken);
    }

    // [2] POST /api/auth/login — FindByName + CheckPassword + phát token.
    public async Task<TokenResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        // BƯỚC 1: Tìm user theo UserName (Identity normalize theo cấu hình).
        var user = await _authRepository.GetByUserNameAsync(request.UserName, cancellationToken);

        // TRƯỜNG HỢP A: User không tồn tại — cùng thông điệp với mật khẩu sai (không lộ tồn tại tài khoản).
        if (user is null)
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.LoginFailed,
                ApiMessages.LoginFailed);
        }

        // BƯỚC 2: Kiểm tra mật khẩu — so hash.
        if (!await _authRepository.ValidatePasswordAsync(user, request.Password, cancellationToken))
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.LoginFailed,
                ApiMessages.LoginFailed);
        }

        // BƯỚC 3: Phát cặp token mới.
        var roles = await _authRepository.GetRoleNamesAsync(user, cancellationToken);
        return await CreateTokenPairAsync(user, roles, cancellationToken);
    }

    // [3] POST /api/auth/refresh — validate refresh JWT, so stamp, phát cặp token mới.
    public async Task<TokenResponseDto> RefreshAsync(RefreshRequestDto request, CancellationToken cancellationToken = default)
    {
        // BƯỚC 1: Parse + validate chữ ký + lifetime + issuer/audience + claim token_type = refresh.
        var principal = ValidateRefreshTokenPrincipal(request.RefreshToken);

        // TRƯỜNG HỢP B: Token không parse được hoặc không phải refresh type.
        if (principal is null)
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.RefreshFailed,
                ApiMessages.RefreshFailed);
        }

        // BƯỚC 2: Đọc claim sub (user id) — bắt buộc là Guid.
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (sub is null || !Guid.TryParse(sub, out var userId))
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.RefreshFailed,
                ApiMessages.RefreshFailed);
        }

        // BƯỚC 3: Tải user hiện tại từ DB.
        var user = await _authRepository.GetByIdAsync(userId, cancellationToken);

        // TRƯỜNG HỢP C: User đã bị xóa.
        if (user is null)
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.RefreshFailed,
                ApiMessages.RefreshFailed);
        }

        // BƯỚC 4: So khớp security stamp trong token với stamp hiện tại trên user — sau đổi mật khẩu / logout stamp đổi.
        var stampInToken = principal.FindFirstValue(JwtOptions.SecurityStampClaimType);
        var currentStamp = await _authRepository.GetSecurityStampAsync(user, cancellationToken);
        if (string.IsNullOrEmpty(stampInToken) || stampInToken != currentStamp)
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.RefreshFailed,
                ApiMessages.RefreshFailed);
        }

        // BƯỚC 5: Phát cặp token mới (rotation refresh).
        var roles = await _authRepository.GetRoleNamesAsync(user, cancellationToken);
        return await CreateTokenPairAsync(user, roles, cancellationToken);
    }

    // [4] POST /api/auth/logout — xoay security stamp nếu user còn tồn tại; idempotent nếu user null.
    public async Task LogoutAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _authRepository.GetByIdAsync(userId, cancellationToken); // Tải user theo Id từ claim.

        if (user is not null) // TRƯỜNG HỢP: user còn tồn tại.
        {
            await _authRepository.RevokeSessionsAsync(user, cancellationToken); // UpdateSecurityStampAsync.
        }
        // TRƯỜNG HỢP: user null — không ném lỗi (logout idempotent theo thiết kế API).
    }

    #endregion

    #region Helpers

    // Tạo access + refresh: cùng security stamp; access có role, refresh không thêm claim role.
    private async Task<TokenResponseDto> CreateTokenPairAsync(User user, IReadOnlyList<string> roles, CancellationToken cancellationToken)
    {
        // BƯỚC 1: Đọc stamp — bắt buộc để ghi vào cả hai token cho bước refresh so khớp sau này.
        var stamp = await _authRepository.GetSecurityStampAsync(user, cancellationToken);
        if (string.IsNullOrEmpty(stamp)) // TRƯỜNG HỢP: Identity chưa có stamp (bất thường).
        {
            throw new ApiException(
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.TokenIssueFailed,
                ApiMessages.TokenIssueFailed);
        }

        // BƯỚC 2: Tính thời điểm hết hạn UTC cho access (ngắn) và refresh (dài).
        var accessExpires = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenMinutes);
        var refreshExpires = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays);

        // BƯỚC 3: Ký hai JWT riêng — access kèm roles, refresh không kèm role.
        var accessToken = CreateJwt(user, roles, accessExpires, AccessTokenType, stamp);
        var refreshToken = CreateJwt(user, Array.Empty<string>(), refreshExpires, RefreshTokenType, stamp);

        // BƯỚC 4: Gói DTO trả controller.
        return new TokenResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAtUtc = accessExpires,
            RefreshTokenExpiresAtUtc = refreshExpires,
        };
    }

    // Tạo một chuỗi JWT: claims sub, token_type, jti, security stamp; access thêm UniqueName + role.
    private string CreateJwt(User user, IReadOnlyList<string> roles, DateTime expiresUtc, string tokenType, string securityStamp)
    {
        // BƯỚC 1: Khóa đối xứng từ cấu hình — UTF8 bytes.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));

        // BƯỚC 2: SigningCredentials HMAC-SHA256.
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // BƯỚC 3: Handler + thời điểm phát hành + jti ngẫu nhiên (có thể dùng revoke list sau này).
        var handler = new JwtSecurityTokenHandler();
        var now = DateTime.UtcNow;
        var jti = Guid.NewGuid().ToString();

        // BƯỚC 4: Claims cốt lõi chung mọi loại token.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(TokenTypeClaim, tokenType),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtOptions.SecurityStampClaimType, securityStamp),
        };

        // BƯỚC 5: CHỈ access — thêm tên hiển thị đăng nhập và từng role (Authorize Roles=...).
        if (tokenType == AccessTokenType)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty));
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        // BƯỚC 6: Dựng JwtSecurityToken và WriteToken — payload JSON chứa đủ claim tùy chỉnh.
        var token = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, now, expiresUtc, creds);
        return handler.WriteToken(token);
    }

    // Validate refresh: chữ ký, issuer, audience, lifetime; bắt buộc claim token_type = refresh; mọi lỗi → null.
    private ClaimsPrincipal? ValidateRefreshTokenPrincipal(string refreshToken)
    {
        // BƯỚC 1: MapInboundClaims = false để claim "sub" giữ tên JWT gốc (tránh map sang NameIdentifier làm lệch FindFirstValue(Sub)).
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        // BƯỚC 2: Cùng khóa ký với lúc tạo token.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));

        try
        {
            // BƯỚC 3: ValidateToken — sai chữ ký / hết hạn / sai iss/aud → throw.
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

            // BƯỚC 4: Kiểm tra loại token — từ chối nếu client gửi access token vào refresh.
            var type = principal.FindFirstValue(TokenTypeClaim);
            if (type != RefreshTokenType)
            {
                return null;
            }

            return principal;
        }
        catch
        {
            // TRƯỜNG HỢP: bất kỳ lỗi validate — trả null để RefreshAsync trả 401 thống nhất.
            return null;
        }
    }

    #endregion
}
