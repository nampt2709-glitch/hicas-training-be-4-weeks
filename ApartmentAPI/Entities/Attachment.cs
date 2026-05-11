// File: entity file đính kèm — scope Avatar hoặc Feedback; FK User/Feedback tuỳ scope.
namespace ApartmentAPI.Entities;

// Metadata file: tên gốc, tên lưu, đường dẫn, content-type, size, hash tuỳ chọn.
public class Attachment : BaseEntity
{ // Mở khối Attachment.
    public AttachmentScope Scope { get; set; } // Phân loại: avatar hoặc feedback.

    public string OriginalFileName { get; set; } = string.Empty; // Tên file người dùng gửi.
    public string StoredFileName { get; set; } = string.Empty; // Tên file trên đĩa (unique/an toàn).
    public string FilePath { get; set; } = string.Empty; // Đường dẫn tương đối hoặc absolute tuỳ cấu hình.
    public string ContentType { get; set; } = string.Empty; // MIME type.
    public long FileSize { get; set; } // Kích thước byte.
    public string? FileHash { get; set; } // Hash integrity/trùng lặp (tuỳ chọn).

    public Guid? UserId { get; set; } // FK uploader (nullable, ví dụ file hệ thống).
    public User? User { get; set; } // Navigation User.

    public Guid? FeedbackId { get; set; } // FK feedback (nullable nếu scope khác).
    public Feedback? Feedback { get; set; } // Navigation Feedback.

    public Guid? PostId { get; set; } // FK bài đăng khi Scope = Post.
    public Post? Post { get; set; } // Navigation Post.
} // Kết thúc Attachment.
