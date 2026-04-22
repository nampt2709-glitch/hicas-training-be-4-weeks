namespace CommentAPI.Entities;

// Bình luận: ForeignKey PostId, UserId; cây qua ParentId/Children; virtual cho lazy proxy khi bật UseLazyLoadingProxies.
public class Comment
{
    public Guid Id { get; set; } // Khóa chính comment, Guid tạo khi insert.
    public string Content { get; set; } = string.Empty; // Nội dung bình luận dạng văn bản.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Thời điểm tạo, UTC mặc định tại thời điểm vật thể tạo.

    public Guid PostId { get; set; } // Khóa ngoại tới bài viết thuộc về, mọi comment phải nằm trong đúng post.
    public virtual Post? Post { get; set; } // Navigation tùy chọn: nạp bài chứa comment, virtual cho lazy.

    public Guid UserId { get; set; } // Id tác giả bình luận (tham chiếu Users).
    public virtual User? User { get; set; } // Navigation tùy chọn: nạp user viết, virtual cho proxy.

    public Guid? ParentId { get; set; } // Id comment cha, null nghĩa là gốc cây trong cùng post.
    public virtual Comment? Parent { get; set; } // Navigation tùy chọn: phía cha, virtual cho path leo cây.

    public virtual ICollection<Comment> Children { get; set; } = new List<Comment>(); // Các bản ghi con (reply), tập dùng cho lazy/INCLUDE.
}
