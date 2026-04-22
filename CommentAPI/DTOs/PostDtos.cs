namespace CommentAPI.DTOs; // Không gian tên: DTO cho post, tách tầng API khỏi entity.

// Thân tạo bài: tiêu đề, nội dung, Id user chủ bài; Admin-only route gán khi tạo.
public class CreatePostDto
{
    public string Title { get; set; } = string.Empty; // Tiêu đề, mặc định rỗng, validator/ service kiểm tra độ dài.
    public string Content { get; set; } = string.Empty; // Nội dung thân bài, mặc định rỗng.
    public Guid UserId { get; set; } // Id tác giả, phải trùng user tồn tại khi tạo.
}

// Tác giả bài: chỉ sửa tiêu đề + nội dung, không đổi user qua DTO này (Admin dùng DTO khác).
public class UpdatePostDto
{
    public string Title { get; set; } = string.Empty; // Tiêu đề cập nhật, mặc định rỗng.
    public string Content { get; set; } = string.Empty; // Nội dung cập nhật, mặc định rỗng.
}

// Admin: cùng cặp text + tùy chọn đổi chủ bài: UserId null nghĩa là không sửa chủ.
public class AdminUpdatePostDto
{
    public string Title { get; set; } = string.Empty; // Tiêu đề, mặc định rỗng; service cập nhật entity.
    public string Content { get; set; } = string.Empty; // Nội dung, mặc định rỗng.
    public Guid? UserId { get; set; } // Nếu có giá trị, gán lại UserId; null giữ nguyên chủ cũ.
}

// Một bài trả về: đủ trường hiển thị list/detail, không navigation đầy đủ.
public class PostDto
{
    public Guid Id { get; set; } // Khóa bài viết, Guid ổn định cho liên kết từ comment.
    public string Title { get; set; } = string.Empty; // Tiêu đề, mặc định rỗng từ map.
    public string Content { get; set; } = string.Empty; // Nội dung, mặc định rỗng từ map.
    public DateTime CreatedAt { get; set; } // Mốc tạo, hiển thị danh sách/ chi tiết.
    public Guid UserId { get; set; } // Id tác giả, dùng kiểm tra quyền phía client nếu cần.
}
