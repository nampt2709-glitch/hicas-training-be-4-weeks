namespace ApartmentAPI.Entities;

// Dòng hóa đơn — liên kết Invoice + UtilityService; số tiền dùng decimal.
public class InvoiceItem : BaseEntity
{
    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    public Guid ServiceId { get; set; }
    public UtilityService Service { get; set; } = null!;

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SubTotal { get; set; }

    public string? Description { get; set; }
}
