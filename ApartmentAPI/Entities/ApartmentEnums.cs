namespace ApartmentAPI.Entities;

// Trạng thái phòng — map số nguyên trong DB.
public enum ApartmentStatus
{
    Available = 0,
    Occupied = 1,
    Maintenance = 2,
}

// Trạng thái hóa đơn — map số nguyên trong DB.
public enum InvoiceStatus
{
    Unpaid = 0,
    Paid = 1,
    Overdue = 2,
    Cancelled = 3,
}

// Phạm vi file đính kèm — avatar / feedback / post.
public enum AttachmentScope
{
    Avatar = 0,
    Feedback = 1,
    Post = 2,
}
