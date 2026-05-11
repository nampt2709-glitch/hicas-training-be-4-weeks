// File: entity căn hộ — quan hệ một-nhiều với Resident và Invoice.
namespace ApartmentAPI.Entities;

// Phòng: số phòng, tầng, diện tích, trạng thái; navigation Residents + Invoices.
public class Apartment : BaseEntity
{ // Mở khối Apartment.
    public string RoomNumber { get; set; } = string.Empty; // Mã/số phòng hiển thị.
    public int Floor { get; set; } // Tầng (số nguyên).
    public decimal Area { get; set; } // Diện tích (đơn vị nghiệp vụ, ví dụ m²).
    public ApartmentStatus Status { get; set; } = ApartmentStatus.Available; // Trạng thái phòng.

    public int MaxResidents { get; set; } // Số cư dân tối đa cho phép.
    public string? Note { get; set; } // Ghi chú nội bộ.

    public ICollection<Resident> Residents { get; set; } = new List<Resident>(); // Cư dân thuộc phòng.
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>(); // Hóa đơn theo phòng.
    public ICollection<Post> Posts { get; set; } = new List<Post>(); // Thông báo / bài đăng gắn căn (tuỳ chọn).
} // Kết thúc Apartment.
