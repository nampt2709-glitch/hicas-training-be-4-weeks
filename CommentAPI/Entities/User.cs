using Microsoft.AspNetCore.Identity;

namespace CommentAPI.Entities;

/// <summary>
/// Một thực thể User duy nhất, map một bảng <c>Users</c> trên SQL Server.
/// Kế thừa <see cref="IdentityUser{TKey}"/> nên có sẵn UserName, Email, SecurityStamp, PasswordHash (cùng bảng).
/// Vai trò (Admin/User) nằm ở bảng liên kết <c>AspNetUserRoles</c> / <c>AspNetRoles</c>, không phải bảng user thứ hai.
/// </summary>
public class User : IdentityUser<Guid>
{
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Navigation ảo cho lazy load từ <see cref="Comment"/> / <see cref="Post"/>.</summary>
    public virtual ICollection<Post> Posts { get; set; } = new List<Post>();

    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();
}
