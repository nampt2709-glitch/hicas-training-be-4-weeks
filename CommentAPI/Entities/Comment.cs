namespace CommentAPI.Entities;

public class Comment
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid PostId { get; set; }
    public Post? Post { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid? ParentId { get; set; }
    public Comment? Parent { get; set; }
    public List<Comment> Children { get; set; } = new();
}
