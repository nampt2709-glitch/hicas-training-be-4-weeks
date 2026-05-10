// File: entity hóa đơn — thuộc một Apartment, chứa nhiều InvoiceItem.
namespace ApartmentAPI.Entities;

// Mã hóa đơn, kỳ tháng/năm, ngày phát hành/hạn/trả, số tiền và trạng thái thanh toán.
public class Invoice : BaseEntity
{ // Mở khối Invoice.
    public string InvoiceCode { get; set; } = string.Empty; // Mã hiển thị duy nhất theo nghiệp vụ.
    public int Month { get; set; } // Tháng kỳ cước (1–12).
    public int Year { get; set; } // Năm kỳ cước.

    public DateTime? IssueDate { get; set; } // Ngày lập hóa đơn.
    public DateTime? DueDate { get; set; } // Hạn thanh toán.
    public DateTime? PaidAt { get; set; } // Thời điểm thanh toán (ghi nhận).

    public decimal TotalAmount { get; set; } // Tổng phải thu (tổng dòng hoặc snapshot).
    public decimal PaidAmount { get; set; } // Đã thu (partial payment).
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid; // Trạng thái workflow.

    public string? Note { get; set; } // Ghi chú kèm hóa đơn.

    public Guid ApartmentId { get; set; } // FK căn hộ (bắt buộc).
    public Apartment Apartment { get; set; } = null!; // Navigation Apartment.

    public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>(); // Chi tiết dịch vụ/dòng tiền.
} // Kết thúc Invoice.
