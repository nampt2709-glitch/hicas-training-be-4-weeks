namespace ApartmentAPI.Entities;

// Cư dân — tùy chọn gắn Apartment hoặc User (tài khoản).
public class Resident : BaseEntity
{
    public string FullName { get; set; } = string.Empty;
    public string IdentityNumber { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateTime? BirthDate { get; set; }

    public bool IsPrimaryResident { get; set; }
    public bool IsActive { get; set; } = true;

    public Guid? ApartmentId { get; set; }
    public Apartment? Apartment { get; set; }

    public Guid? UserId { get; set; }
    public User? User { get; set; }
}
