namespace ApartmentAPI.Entities;

// Refresh token — lưu hash, liên kết User.
public class RefreshToken : BaseEntity
{
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }

    public string? ReplacedByTokenHash { get; set; }
    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
