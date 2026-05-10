// File: cấu hình JWT (issuer, audience, khóa ký, TTL access/refresh, claim security stamp — cùng pattern CommentAPI).
namespace ApartmentAPI;

// Binding section "Jwt": khớp appsettings và DI Options.
public sealed class JwtOptions
{ // Mở khối JwtOptions.
    public const string SectionName = "Jwt"; // Tên section trong IConfiguration.

    public const string SecurityStampClaimType = "sec_stamp"; // Claim để vô hiệu hóa token khi đổi mật khẩu.

    public string Issuer { get; set; } = string.Empty; // Issuer trong JWT.
    public string Audience { get; set; } = string.Empty; // Audience mong đợi.
    public string SigningKey { get; set; } = string.Empty; // Khóa bí mật ký HMAC (Symmetric).
    public int AccessTokenMinutes { get; set; } = 15; // Thời lượng access token (phút).
    public int RefreshTokenDays { get; set; } = 7; // Thời lượng refresh token (ngày).
} // Kết thúc JwtOptions.
