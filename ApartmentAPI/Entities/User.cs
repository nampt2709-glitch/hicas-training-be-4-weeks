using Microsoft.AspNetCore.Identity;

namespace ApartmentAPI.Entities;

// Người dùng — IdentityUser<Guid>; RefreshToken, Feedback, Attachment, Resident.
public class User : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>();
    public ICollection<Attachment> Uploads { get; set; } = new List<Attachment>();
    public ICollection<Resident> Residents { get; set; } = new List<Resident>();
}
