using Microsoft.AspNetCore.Identity; // Namespace chứa IdentityUser<TKey> dùng làm cơ sở cho tài khoản.

// Entity ánh xạ bảng dùng chung với Identity (cùng khóa Guid).
namespace CommentAPI.Entities;

// Tài khoản: kế thừa IdentityUser<Guid> (UserName, Email, SecurityStamp, PasswordHash, v.v.); thêm Name, CreatedAt, quan hệ Post/Comment.
public class User : IdentityUser<Guid>
{
    // Tên hiển thị tùy nghiệp vụ, không bắt buộc trùng UserName.
    public string Name { get; set; } = string.Empty;
    // Mốc tạo bản ghi hồ sơ bổ sung, UTC.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Các bài user đăng; virtual cho lazy nếu bật proxy.
    public virtual ICollection<Post> Posts { get; set; } = new List<Post>();

    // Các bình luận user viết; virtual cho lazy/INCLUDE từ phía user.
    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();
}
