// File: entity người dùng — kế thừa IdentityUser<Guid>, navigation refresh token / feedback / file / resident.
using Microsoft.AspNetCore.Identity; // IdentityUser<Guid>, kiểu khóa Guid.

namespace ApartmentAPI.Entities;

// User ứng dụng: họ tên, avatar, cờ hoạt động; quan hệ một-nhiều với RefreshToken, Feedback, Attachment, Resident.
public class User : IdentityUser<Guid>
{ // Mở khối User.
    public string FullName { get; set; } = string.Empty; // Tên hiển thị ngoài email.
    public string? AvatarUrl { get; set; } // URL ảnh đại diện (tuỳ chọn).
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Thời điểm tạo hồ sơ (UTC).
    public bool IsActive { get; set; } = true; // Khóa đăng nhập mềm khi false.

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>(); // Phiên đăng nhập kéo dài.
    public ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>(); // Phản hồi do user gửi.
    public ICollection<Post> Posts { get; set; } = new List<Post>(); // Bài đăng / thông báo do user tạo.
    public ICollection<Attachment> Uploads { get; set; } = new List<Attachment>(); // File user tải lên.
    public ICollection<Resident> Residents { get; set; } = new List<Resident>(); // Hồ sơ cư dân liên kết.
} // Kết thúc User.
