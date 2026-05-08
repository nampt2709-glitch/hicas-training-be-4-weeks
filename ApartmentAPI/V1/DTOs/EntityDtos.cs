using ApartmentAPI.Entities;

namespace ApartmentAPI.V1.DTOs;

// --- Apartment ---
public class ApartmentDto
{
    public Guid Id { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public int Floor { get; set; }
    public decimal Area { get; set; }
    public ApartmentStatus Status { get; set; }
    public int MaxResidents { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateApartmentDto
{
    public string RoomNumber { get; set; } = string.Empty;
    public int Floor { get; set; }
    public decimal Area { get; set; }
    public ApartmentStatus Status { get; set; } = ApartmentStatus.Available;
    public int MaxResidents { get; set; }
    public string? Note { get; set; }
}

public class UpdateApartmentDto
{
    public string RoomNumber { get; set; } = string.Empty;
    public int Floor { get; set; }
    public decimal Area { get; set; }
    public ApartmentStatus Status { get; set; }
    public int MaxResidents { get; set; }
    public string? Note { get; set; }
}

// --- Resident ---
public class ResidentDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string IdentityNumber { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateTime? BirthDate { get; set; }
    public bool IsPrimaryResident { get; set; }
    public bool IsActive { get; set; }
    public Guid? ApartmentId { get; set; }
    public Guid? UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateResidentDto
{
    public string FullName { get; set; } = string.Empty;
    public string IdentityNumber { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateTime? BirthDate { get; set; }
    public bool IsPrimaryResident { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? ApartmentId { get; set; }
    public Guid? UserId { get; set; }
}

public class UpdateResidentDto : CreateResidentDto
{
}

// --- UtilityService ---
public class UtilityServiceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Unit { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateUtilityServiceDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Unit { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class UpdateUtilityServiceDto : CreateUtilityServiceDto
{
}

// --- Invoice ---
public class InvoiceDto
{
    public Guid Id { get; set; }
    public string InvoiceCode { get; set; } = string.Empty;
    public int Month { get; set; }
    public int Year { get; set; }
    public DateTime? IssueDate { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public InvoiceStatus Status { get; set; }
    public string? Note { get; set; }
    public Guid ApartmentId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateInvoiceDto
{
    public string InvoiceCode { get; set; } = string.Empty;
    public int Month { get; set; }
    public int Year { get; set; }
    public DateTime? IssueDate { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;
    public string? Note { get; set; }
    public Guid ApartmentId { get; set; }
}

public class UpdateInvoiceDto : CreateInvoiceDto
{
}

// --- InvoiceItem ---
public class InvoiceItemDto
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid ServiceId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SubTotal { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateInvoiceItemDto
{
    public Guid InvoiceId { get; set; }
    public Guid ServiceId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SubTotal { get; set; }
    public string? Description { get; set; }
}

public class UpdateInvoiceItemDto : CreateInvoiceItemDto
{
}

// --- Feedback ---
public class FeedbackDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
    public bool IsPinned { get; set; }
    public Guid UserId { get; set; }
    public Guid? ParentId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateFeedbackDto
{
    public string Content { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
    public bool IsPinned { get; set; }
    public Guid UserId { get; set; }
    public Guid? ParentId { get; set; }
}

public class UpdateFeedbackDto
{
    public string Content { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
    public bool IsPinned { get; set; }
}

// --- Attachment ---
public class AttachmentDto
{
    public Guid Id { get; set; }
    public AttachmentScope Scope { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string? FileHash { get; set; }
    public Guid? UserId { get; set; }
    public Guid? FeedbackId { get; set; }
    public Guid? PostId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateAttachmentDto
{
    public AttachmentScope Scope { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string? FileHash { get; set; }
    public Guid? UserId { get; set; }
    public Guid? FeedbackId { get; set; }
    public Guid? PostId { get; set; }
}

public class UpdateAttachmentDto : CreateAttachmentDto
{
}

// --- RefreshToken ---
public class RefreshTokenDto
{
    public Guid Id { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid UserId { get; set; }
    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateRefreshTokenDto
{
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public Guid UserId { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }
}

public class UpdateRefreshTokenDto
{
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
}

// --- User / Role (Identity) ---
public class UserListDto
{
    public Guid Id { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateUserDto
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
}

public class UpdateUserDto
{
    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; } = true;
}

public class RoleDto
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
}

public class CreateRoleDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateRoleDto
{
    public string? Description { get; set; }
}
