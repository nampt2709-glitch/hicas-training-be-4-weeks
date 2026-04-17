namespace CommentAPI;

/// <summary>
/// Cấu hình JWT từ appsettings (issuer, audience, signing key, thời gian sống token).
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Loại claim chứa security stamp của Identity (kiểm tra sau đổi stamp / logout).</summary>
    public const string SecurityStampClaimType = "sec_stamp";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}
