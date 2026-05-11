// File: entity bài đăng / thông báo nội bộ — tác giả User, tuỳ chọn gắn một căn hộ; file đính kèm qua Attachment (Scope = Post).
namespace ApartmentAPI.Entities;

// Bài viết: tiêu đề, nội dung, cờ xuất bản; quan hệ một-nhiều với Attachment (đính kèm bài).
public class Post : BaseEntity
{ // Mở khối Post.
    public string Title { get; set; } = string.Empty; // Tiêu đề hiển thị.
    public string Content { get; set; } = string.Empty; // Nội dung (văn bản/markdown tùy client).

    public Guid UserId { get; set; } // FK tác giả — bắt buộc.
    public User User { get; set; } = null!; // Navigation tới User.

    public Guid? ApartmentId { get; set; } // Gắn căn hụ cụ thể; null = thông báo chung khu.
    public Apartment? Apartment { get; set; } // Navigation căn hộ.

    public bool IsPublished { get; set; } // Hiển thị công khai cho cư dân / API list.

    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>(); // File đính kèm bài.
} // Kết thúc Post.
