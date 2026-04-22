namespace CommentAPI;

// Cấu hình JWT đọc từ appsettings: issuer, audience, khóa ký, thời gian sống access/refresh; bind bằng IOptions.
public class JwtOptions
{
    // Tên section trong IConfiguration (GetSection) — phải khớp với JSON.
    public const string SectionName = "Jwt";

    // Tên custom claim lưu security stamp Identity; đổi sau đổi mật khẩu/đổi stamp thì token cũ bị từ chối.
    public const string SecurityStampClaimType = "sec_stamp";

    // Phát hành JWT: giá trị "iss" phải trùng khi validate.
    public string Issuer { get; set; } = string.Empty;

    // Đích dự kiến: claim "aud" khi cấu hình ValidateAudience = true.
    public string Audience { get; set; } = string.Empty;

    // Chuỗi bí mật dài đủ cho HMAC-SHA (khóa ký simmetric), không dùng chuỗi ngắn ở production.
    public string SigningKey { get; set; } = string.Empty;

    // Số phút sống của access token (mặc định 15 nếu JSON không ghi).
    public int AccessTokenMinutes { get; set; } = 15;

    // Số ngày sống refresh token (lưu server-side hoặc bảng, tùy implement auth).
    public int RefreshTokenDays { get; set; } = 7;
}
