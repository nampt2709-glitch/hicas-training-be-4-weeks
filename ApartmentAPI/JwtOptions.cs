namespace ApartmentAPI;

// Cấu hình JWT đọc từ appsettings section Jwt.
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
}
