namespace ApartmentAPI.Entities;

// Phản hồi — cây cha-con, Attachments, User tác giả.
public class Feedback : BaseEntity
{
    public string Content { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
    public bool IsPinned { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid? ParentId { get; set; }
    public Feedback? Parent { get; set; }
    public ICollection<Feedback> Children { get; set; } = new List<Feedback>();

    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
}
