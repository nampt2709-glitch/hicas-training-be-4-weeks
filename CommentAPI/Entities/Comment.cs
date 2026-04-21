namespace CommentAPI.Entities;

/// <summary>
/// Thực thể Comment; navigation <c>virtual</c> + <see cref="ICollection{T}"/> để EF Core lazy-loading proxies hoạt động (route demo).
/// </summary>
public class Comment
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid PostId { get; set; }
    public virtual Post? Post { get; set; }

    public Guid UserId { get; set; }
    public virtual User? User { get; set; }

    public Guid? ParentId { get; set; }
    public virtual Comment? Parent { get; set; }

    public virtual ICollection<Comment> Children { get; set; } = new List<Comment>();
}
