namespace CommentAPI.Entities;

/// <summary>Bài viết; navigation <c>virtual</c> phục vụ lazy-loading proxies khi load từ <see cref="Comment"/>.</summary>
public class Post
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid UserId { get; set; }
    public virtual User? User { get; set; }

    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();
}
