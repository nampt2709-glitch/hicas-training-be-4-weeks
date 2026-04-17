namespace CommentAPI.DTOs;

public class CreateCommentDto
{
    public string Content { get; set; } = string.Empty;
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ParentId { get; set; }
}

public class UpdateCommentDto
{
    public string Content { get; set; } = string.Empty;
}

public class CommentDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ParentId { get; set; }
}

public class CommentFlatDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid PostId { get; set; }
    public Guid? ParentId { get; set; }
    public int Level { get; set; }
}

public class CommentTreeDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid PostId { get; set; }
    public Guid? ParentId { get; set; }
    public List<CommentTreeDto> Children { get; set; } = new();
}
