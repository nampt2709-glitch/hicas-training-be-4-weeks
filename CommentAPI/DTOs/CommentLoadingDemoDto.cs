namespace CommentAPI.DTOs; // DTO demo chiến lược nạp dữ liệu, không dùng cho CRUD sản xuất chính.

// Một bản ghi tổng hợp: đủ dữ liệu mô tả một cách nạp (lazy, eager, explicit, projection) cho route demo.
public sealed class CommentLoadingDemoDto
{
    public string LoadingStrategy { get; init; } = string.Empty; // Nhãn dạng chữ: "lazy" | "eager" | "explicit" | "projection" — biết cách hình thành dữ liệu.
    public Guid CommentId { get; init; } // Id comment, khớp bảng Comments, khóa phân biệt dòng.
    public string Content { get; init; } = string.Empty; // Nội dung, mặc định rỗng, đọc từ cột tương ứng.
    public Guid PostId { get; init; } // Bài, liên kết tới bài chứa comment.
    public string? PostTitle { get; init; } // Tiêu đề bài nếu đã tải hoặc Select; null nếu chưa có trong payload.
    public Guid UserId { get; init; } // Id tác giả, map bảng Users.
    public string? AuthorUserName { get; init; } // Tên đăng nhập tác giả nếu đã tải User, null nếu không nạp sẵn.
    public Guid? ParentId { get; init; } // null = gốc; có giá trị = id cha trong cùng bài.
    public int ChildrenCount { get; init; } // Số bản ghi con trực tiếp hoặc quy ước theo từng strategy demo, trả ổn định theo từng lần gọi.
}
