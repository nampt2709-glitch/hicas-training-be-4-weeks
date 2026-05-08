namespace ApartmentAPI.Entities;

// File đính kèm — Avatar / Feedback / Post (PostId dự trữ).
public class Attachment : BaseEntity
{
    public AttachmentScope Scope { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string? FileHash { get; set; }

    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public Guid? FeedbackId { get; set; }
    public Feedback? Feedback { get; set; }

    public Guid? PostId { get; set; }
}
