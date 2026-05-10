// File: entity cư dân — tùy chọn gắn Apartment và/hoặc User (tài khoản đăng nhập).
namespace ApartmentAPI.Entities;

// Thông tin cá nhân + khóa ngoại tuỳ chọn ApartmentId, UserId.
public class Resident : BaseEntity
{ // Mở khối Resident.
    public string FullName { get; set; } = string.Empty; // Họ tên đầy đủ.
    public string IdentityNumber { get; set; } = string.Empty; // CMND/CCCD/passport (chuỗi).
    public string PhoneNumber { get; set; } = string.Empty; // Số điện thoại liên hệ.
    public string? Email { get; set; } // Email (tuỳ chọn).
    public DateTime? BirthDate { get; set; } // Ngày sinh (tuỳ chọn).

    public bool IsPrimaryResident { get; set; } // Người đại diện hợp đồng / chủ hộ.
    public bool IsActive { get; set; } = true; // Còn hiệu lực cư trú.

    public Guid? ApartmentId { get; set; } // FK tới Apartment (null nếu chưa gán phòng).
    public Apartment? Apartment { get; set; } // Navigation tới căn hộ.

    public Guid? UserId { get; set; } // FK tới User Identity (null nếu chưa liên kết tài khoản).
    public User? User { get; set; } // Navigation tới người dùng.
} // Kết thúc Resident.
