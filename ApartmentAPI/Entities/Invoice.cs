namespace ApartmentAPI.Entities;

// Hóa đơn — thuộc một Apartment, có nhiều InvoiceItem.
public class Invoice : BaseEntity
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
    public Apartment Apartment { get; set; } = null!;

    public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
}
