// File: entity cơ sở — audit + soft delete chung (không dùng cho User Identity).
namespace ApartmentAPI.Entities;

// Cột Id/Created/Updated/Deleted chuẩn cho bảng nghiệp vụ ApartmentAPI.
public abstract class BaseEntity
{ // Mở khối BaseEntity.
    public Guid Id { get; set; } = Guid.NewGuid(); // Khóa chính, sinh sẵn khi new.

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Thời điểm tạo (UTC).
    public string? CreatedBy { get; set; } // Người/claims tạo (nullable).

    public DateTime? UpdatedAt { get; set; } // Thời điểm cập nhật cuối (UTC).
    public string? UpdatedBy { get; set; } // Người cập nhật cuối.

    public bool IsDeleted { get; set; } // Cờ soft delete: true = ẩn khỏi truy vấn mặc định.
    public DateTime? DeletedAt { get; set; } // Thời điểm xóa mềm.
    public string? DeletedBy { get; set; } // Người thực hiện xóa mềm.
} // Kết thúc BaseEntity.
