namespace ApartmentAPI.Entities;

// Dịch vụ tiện ích — nguồn cho InvoiceItem.
public class UtilityService : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Unit { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
