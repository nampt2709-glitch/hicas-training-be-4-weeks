// File: entity phản hồi — cây cha-con, đính kèm, tác giả User.
namespace ApartmentAPI.Entities;

// Nội dung phản hồi + ParentId (thread), cờ resolved/pinned, collection Attachments.
public class Feedback : BaseEntity
{ // Mở khối Feedback.
    public string Content { get; set; } = string.Empty; // Nội dung văn bản.
    public bool IsResolved { get; set; } // Đã xử lý xong.
    public bool IsPinned { get; set; } // Ghim lên đầu danh sách.

    public Guid UserId { get; set; } // FK tác giả (bắt buộc).
    public User User { get; set; } = null!; // Navigation tới User.

    public Guid? ParentId { get; set; } // FK comment cha (null = gốc thread).
    public Feedback? Parent { get; set; } // Navigation cha.
    public ICollection<Feedback> Children { get; set; } = new List<Feedback>(); // Con trực tiếp.

    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>(); // File đính kèm feedback.
} // Kết thúc Feedback.
