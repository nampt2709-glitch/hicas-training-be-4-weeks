// Cùng assembly entity với User/Comment cho AppDbContext.
namespace CommentAPI.Entities;

// Một bài viết: UserId, tiêu đề, nội dung, tập Comments; virtual hỗ trợ proxy lazy từ phía comment.
public class Post
{
    public Guid Id { get; set; } // Khóa chính bài viết, Guid do ứng dụng cấp khi tạo.
    public string Title { get; set; } = string.Empty; // Tiêu đề, chuỗi rỗng là mặc định an toàn trước gán từ DTO.
    public string Content { get; set; } = string.Empty; // Nội dung thân bài, mặc định rỗng.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Mốc tạo bản ghi, UTC để thống nhất múi giờ.

    public Guid UserId { get; set; } // Id người tạo bài (tham chiếu tới bảng Users / Identity user).
    public virtual User? User { get; set; } // Navigation tùy chọn: nạp user chủ bài, virtual cho proxy lazy nếu bật.

    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>(); // Tập comment con, khởi tạo list rỗng để tránh null, virtual cho proxy.
}
