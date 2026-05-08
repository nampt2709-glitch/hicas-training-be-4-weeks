namespace ApartmentAPI.Entities;

// Căn hộ — Residents + Invoices.
public class Apartment : BaseEntity
{
    public string RoomNumber { get; set; } = string.Empty;
    public int Floor { get; set; }
    public decimal Area { get; set; }
    public ApartmentStatus Status { get; set; } = ApartmentStatus.Available;

    public int MaxResidents { get; set; }
    public string? Note { get; set; }

    public ICollection<Resident> Residents { get; set; } = new List<Resident>();
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}
