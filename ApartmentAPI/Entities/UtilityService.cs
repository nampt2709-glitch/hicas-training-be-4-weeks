// File: entity dịch vụ tiện ích — danh mục cho InvoiceItem (tên, giá, đơn vị).
namespace ApartmentAPI.Entities;

// Dịch vụ có thể bật/tắt (IsActive); Price + Unit cho tính tiền trên dòng hóa đơn.
public class UtilityService : BaseEntity
{ // Mở khối UtilityService.
    public string Name { get; set; } = string.Empty; // Tên hiển thị dịch vụ.
    public string? Description { get; set; } // Mô tả dài (tuỳ chọn).
    public decimal Price { get; set; } // Đơn giá mặc định (có thể override trên InvoiceItem).
    public string Unit { get; set; } = string.Empty; // Đơn vị tính (kWh, m³, tháng, v.v.).
    public bool IsActive { get; set; } = true; // Ẩn khỏi chọn mới khi false.
} // Kết thúc UtilityService.
