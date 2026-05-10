// File: entity dòng hóa đơn — liên kết Invoice + UtilityService; tiền tệ decimal.
namespace ApartmentAPI.Entities;

// Một dịch vụ trên hóa đơn: số lượng, đơn giá, thành tiền; mô tả tuỳ chọn.
public class InvoiceItem : BaseEntity
{ // Mở khối InvoiceItem.
    public Guid InvoiceId { get; set; } // FK hóa đơn cha.
    public Invoice Invoice { get; set; } = null!; // Navigation Invoice.

    public Guid ServiceId { get; set; } // FK UtilityService (dịch vụ tiện ích).
    public UtilityService Service { get; set; } = null!; // Navigation dịch vụ.

    public decimal Quantity { get; set; } // Số lượng (có thể là số thập phân, ví dụ kWh).
    public decimal UnitPrice { get; set; } // Đơn giá một đơn vị.
    public decimal SubTotal { get; set; } // quantity * unitPrice (snapshot).

    public string? Description { get; set; } // Ghi chú dòng (ghi đè tên dịch vụ nếu cần).
} // Kết thúc InvoiceItem.
