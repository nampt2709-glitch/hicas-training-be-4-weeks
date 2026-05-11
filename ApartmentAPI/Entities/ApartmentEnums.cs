// File: enum nghiệp vụ — map số nguyên trong cột DB (Apartment, Invoice, Attachment).
namespace ApartmentAPI.Entities;

// Trạng thái căn hộ — đồng bộ với cột int trên bảng Apartment.
public enum ApartmentStatus
{ // Mở khối ApartmentStatus.
    Available = 0, // Còn trống / sẵn sàng cho thuê.
    Occupied = 1, // Đang có cư dân.
    Maintenance = 2, // Bảo trì, không gán cư dân.
} // Kết thúc ApartmentStatus.

// Trạng thái hóa đơn — đồng bộ với cột int trên bảng Invoice.
public enum InvoiceStatus
{ // Mở khối InvoiceStatus.
    Unpaid = 0, // Chưa thanh toán đủ.
    Paid = 1, // Đã thanh toán.
    Overdue = 2, // Quá hạn.
    Cancelled = 3, // Đã hủy.
} // Kết thúc InvoiceStatus.

// Phạm vi file đính kèm — avatar, phản hồi hoặc bài đăng (Post).
public enum AttachmentScope
{ // Mở khối AttachmentScope.
    Avatar = 0, // Ảnh đại diện user.
    Feedback = 1, // Đính kèm phản hồi.
    Post = 2, // Đính kèm bài đăng / thông báo.
} // Kết thúc AttachmentScope.
