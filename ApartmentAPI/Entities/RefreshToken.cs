// File: entity refresh token — hash token, hết hạn, thu hồi, rotation metadata, FK User.
namespace ApartmentAPI.Entities;

// Lưu hash an toàn (không lưu token thô); hỗ trợ revoke và chuỗi thay thế.
public class RefreshToken : BaseEntity
{ // Mở khối RefreshToken.
    public string TokenHash { get; set; } = string.Empty; // Hash SHA/thuật toán ứng dụng dùng để so khớp cookie/body.

    public DateTime ExpiresAt { get; set; } // Thời điểm hết hạn tuyệt đối.
    public bool IsRevoked { get; set; } // Đã thu hồi (logout, rotation, nghi ngờ leak).
    public DateTime? RevokedAt { get; set; } // Thời điểm revoke.

    public string? ReplacedByTokenHash { get; set; } // Hash token mới khi rotation.
    public string? DeviceInfo { get; set; } // UA hoặc nhãn thiết bị (audit).
    public string? IpAddress { get; set; } // IP lúc cấp (audit).

    public Guid UserId { get; set; } // FK chủ sở hữu token.
    public User User { get; set; } = null!; // Navigation User.
} // Kết thúc RefreshToken.
