namespace CommentAPI.DTOs;

/// <summary>
/// Các cột user cần cho GET phân trang/tìm kiếm — tránh SELECT * và cột nhạy cảm (PasswordHash, v.v.).
/// </summary>
public sealed record UserPageRow(Guid Id, string Name, string UserName, string? Email, DateTime CreatedAt);
