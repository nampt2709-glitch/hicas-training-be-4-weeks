namespace CommentAPI.DTOs;

/// <summary>
/// Payload demo: một dòng comment + Post/User/đếm con — lazy, eager, explicit hoặc projection (Select SQL).
/// </summary>
public sealed class CommentLoadingDemoDto
{
    /// <summary>lazy | eager | explicit | projection</summary>
    public string LoadingStrategy { get; init; } = string.Empty;

    public Guid CommentId { get; init; }
    public string Content { get; init; } = string.Empty;
    public Guid PostId { get; init; }
    public string? PostTitle { get; init; }
    public Guid UserId { get; init; }
    public string? AuthorUserName { get; init; }
    public Guid? ParentId { get; init; }
    public int ChildrenCount { get; init; }
}
