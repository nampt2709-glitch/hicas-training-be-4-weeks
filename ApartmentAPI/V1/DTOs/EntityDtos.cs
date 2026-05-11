// File EntityDtos.cs — DTO trao đổi API V1 cho entity căn hộ (AutoMapper trong V1.MappingProfile).
// Mỗi nhóm căn hộ / cư dân / tiện ích / … có #region riêng và ghi chú tiếng Việt có dấu theo khối.
using ApartmentAPI.Entities; // Enum trạng thái căn hộ, hóa đơn, phạm vi đính kèm, v.v.

namespace ApartmentAPI.V1.DTOs;

#region Apartment — căn hộ

// DTO trả về: căn hộ đã có Id và CreatedAt từ DB.
public class ApartmentDto
{
    public Guid Id { get; set; } // Khóa chính.
    public string RoomNumber { get; set; } = string.Empty; // Số phòng.
    public int Floor { get; set; } // Tầng.
    public decimal Area { get; set; } // Diện tích (m²).
    public ApartmentStatus Status { get; set; } // Trạng thái (trống / đã thuê / bảo trì…).
    public int MaxResidents { get; set; } // Giới hạn số người ở.
    public string? Note { get; set; } // Ghi chú tùy chọn.
    public DateTime CreatedAt { get; set; } // Thời điểm tạo (UTC theo chiến lược DB).
}

// DTO nhận khi POST tạo mới căn hộ (chưa có Id).
public class CreateApartmentDto
{
    public string RoomNumber { get; set; } = string.Empty; // Số phòng bắt buộc.
    public int Floor { get; set; } // Tầng.
    public decimal Area { get; set; } // Diện tích > 0.
    public ApartmentStatus Status { get; set; } = ApartmentStatus.Available; // Mặc định còn trống.
    public int MaxResidents { get; set; } // Số người tối đa.
    public string? Note { get; set; } // Ghi chú.
}

// DTO nhận khi PUT cập nhật căn hộ (toàn bộ trường có thể sửa).
public class UpdateApartmentDto
{
    public string RoomNumber { get; set; } = string.Empty; // Số phòng.
    public int Floor { get; set; } // Tầng.
    public decimal Area { get; set; } // Diện tích.
    public ApartmentStatus Status { get; set; } // Trạng thái mới.
    public int MaxResidents { get; set; } // Giới hạn cư dân.
    public string? Note { get; set; } // Ghi chú.
}

#endregion

#region Resident — cư dân

public class ResidentDto
{
    public Guid Id { get; set; } // Khóa chính cư dân.
    public string FullName { get; set; } = string.Empty; // Họ và tên.
    public string IdentityNumber { get; set; } = string.Empty; // CMND/CCCD.
    public string PhoneNumber { get; set; } = string.Empty; // Điện thoại.
    public string? Email { get; set; } // Email tùy chọn.
    public DateTime? BirthDate { get; set; } // Ngày sinh.
    public bool IsPrimaryResident { get; set; } // Chủ hộ hay không.
    public bool IsActive { get; set; } // Còn hiệu lực.
    public Guid? ApartmentId { get; set; } // Căn hộ gắn (null nếu chưa gán).
    public Guid? UserId { get; set; } // Tài khoản Identity liên kết.
    public DateTime CreatedAt { get; set; } // Thời điểm tạo.
}

public class CreateResidentDto
{
    public string FullName { get; set; } = string.Empty; // Họ tên.
    public string IdentityNumber { get; set; } = string.Empty; // Giấy tờ.
    public string PhoneNumber { get; set; } = string.Empty; // SĐT.
    public string? Email { get; set; } // Email.
    public DateTime? BirthDate { get; set; } // Sinh nhật.
    public bool IsPrimaryResident { get; set; } // Chủ hộ.
    public bool IsActive { get; set; } = true; // Mặc định đang hoạt động.
    public Guid? ApartmentId { get; set; } // Căn hộ.
    public Guid? UserId { get; set; } // User.
}

// Cập nhật cư dân: giữ cùng trường với tạo (kế thừa CreateResidentDto).
public class UpdateResidentDto : CreateResidentDto
{
}

#endregion

#region UtilityService — dịch vụ tiện ích

public class UtilityServiceDto
{
    public Guid Id { get; set; } // Khóa dịch vụ.
    public string Name { get; set; } = string.Empty; // Tên (điện, nước, v.v.).
    public string? Description { get; set; } // Mô tả.
    public decimal Price { get; set; } // Đơn giá.
    public string Unit { get; set; } = string.Empty; // Đơn vị tính (kWh, m³…).
    public bool IsActive { get; set; } // Còn áp dụng.
    public DateTime CreatedAt { get; set; } // Tạo lúc nào.
}

public class CreateUtilityServiceDto
{
    public string Name { get; set; } = string.Empty; // Tên.
    public string? Description { get; set; } // Mô tả.
    public decimal Price { get; set; } // Giá ≥ 0.
    public string Unit { get; set; } = string.Empty; // Đơn vị.
    public bool IsActive { get; set; } = true; // Mặc định bật.
}

public class UpdateUtilityServiceDto : CreateUtilityServiceDto
{
}

#endregion

#region Invoice — hóa đơn

public class InvoiceDto
{
    public Guid Id { get; set; } // Khóa hóa đơn.
    public string InvoiceCode { get; set; } = string.Empty; // Mã hóa đơn hiển thị.
    public int Month { get; set; } // Tháng (1–12).
    public int Year { get; set; } // Năm.
    public DateTime? IssueDate { get; set; } // Ngày phát hành.
    public DateTime? DueDate { get; set; } // Hạn thanh toán.
    public DateTime? PaidAt { get; set; } // Thời điểm đã thanh toán đủ.
    public decimal TotalAmount { get; set; } // Tổng phải thu.
    public decimal PaidAmount { get; set; } // Đã thanh toán (một phần hoặc đủ).
    public InvoiceStatus Status { get; set; } // Trạng thái (chưa trả / đã trả…).
    public string? Note { get; set; } // Ghi chú.
    public Guid ApartmentId { get; set; } // Căn hộ liên quan.
    public DateTime CreatedAt { get; set; } // Tạo bản ghi.
}

public class CreateInvoiceDto
{
    public string InvoiceCode { get; set; } = string.Empty; // Mã duy nhất theo validator.
    public int Month { get; set; } // Tháng.
    public int Year { get; set; } // Năm.
    public DateTime? IssueDate { get; set; } // Ngày lập.
    public DateTime? DueDate { get; set; } // Hạn.
    public DateTime? PaidAt { get; set; } // Ngày trả (nếu có).
    public decimal TotalAmount { get; set; } // Tổng tiền.
    public decimal PaidAmount { get; set; } // Đã trả.
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid; // Mặc định chưa trả.
    public string? Note { get; set; } // Ghi chú.
    public Guid ApartmentId { get; set; } // FK căn hộ.
}

public class UpdateInvoiceDto : CreateInvoiceDto
{
}

#endregion

#region InvoiceItem — dòng hóa đơn

public class InvoiceItemDto
{
    public Guid Id { get; set; } // Khóa dòng.
    public Guid InvoiceId { get; set; } // Thuộc hóa đơn nào.
    public Guid ServiceId { get; set; } // Dịch vụ tiện ích.
    public decimal Quantity { get; set; } // Số lượng.
    public decimal UnitPrice { get; set; } // Đơn giá tại thời điểm lập.
    public decimal SubTotal { get; set; } // Thành tiền (thường = quantity × unitPrice).
    public string? Description { get; set; } // Chi tiết dòng (tùy chọn).
    public DateTime CreatedAt { get; set; } // Tạo khi nào.
}

public class CreateInvoiceItemDto
{
    public Guid InvoiceId { get; set; } // Hóa đơn cha.
    public Guid ServiceId { get; set; } // Dịch vụ.
    public decimal Quantity { get; set; } // SL.
    public decimal UnitPrice { get; set; } // Đơn giá.
    public decimal SubTotal { get; set; } // Tiền dòng.
    public string? Description { get; set; } // Mô tả.
}

public class UpdateInvoiceItemDto : CreateInvoiceItemDto
{
}

#endregion

#region Feedback — phản hồi

public class FeedbackDto
{
    public Guid Id { get; set; } // Khóa phản hồi.
    public string Content { get; set; } = string.Empty; // Nội dung.
    public bool IsResolved { get; set; } // Đã xử lý xong.
    public bool IsPinned { get; set; } // Ghim nổi bật.
    public Guid UserId { get; set; } // Người gửi.
    public Guid? ParentId { get; set; } // Phản hồi cha (cây trả lời).
    public DateTime CreatedAt { get; set; } // Thời gian tạo.
}

// Một dòng phẳng từ SqlQueryRaw CTE đệ quy (cùng họ route với CommentAPI CommentCteDto; không có PostId — cây toàn hệ).
public class FeedbackCteDto
{ // Mở khối FeedbackCteDto — projection EF Core sau EXEC CTE.
    public Guid Id { get; set; } // Khóa nút feedback.
    public string Content { get; set; } = string.Empty; // Nội dung.
    public DateTime CreatedAt { get; set; } // Thời điểm tạo.
    public Guid UserId { get; set; } // Tác giả.
    public Guid? ParentId { get; set; } // Cha trong cây (null = gốc).
    public bool IsResolved { get; set; } // Cờ đã xử lý.
    public bool IsPinned { get; set; } // Cờ ghim.
    public int Level { get; set; } // Độ sâu do CTE gán (0 = gốc).
} // Kết thúc FeedbackCteDto.

// Cây lồng sau BuildFeedbackTreeCte — payload GET .../feedbacks/tree/cte (mirror CommentTreeCteDto).
public class FeedbackTreeCteDto
{ // Mở khối FeedbackTreeCteDto — nút có danh sách con đệ quy.
    public Guid Id { get; set; } // Id nút.
    public string Content { get; set; } = string.Empty; // Nội dung.
    public DateTime CreatedAt { get; set; } // Thời tạo.
    public Guid UserId { get; set; } // Tác giả.
    public Guid? ParentId { get; set; } // Cha (tham chiếu phẳng).
    public bool IsResolved { get; set; } // Đã xử lý.
    public bool IsPinned { get; set; } // Ghim.
    public int Level { get; set; } // Độ sâu từ hàng CTE.
    public List<FeedbackTreeCteDto> Children { get; set; } = new(); // Con trực tiếp (cấu trúc cây).
} // Kết thúc FeedbackTreeCteDto.

// Một dòng preorder sau flatten cây CTE — GET .../feedbacks/tree/cte/flatten (mirror CommentFlattenCteDto).
public class FeedbackFlattenCteDto
{ // Mở khối FeedbackFlattenCteDto — danh sách phẳng theo thứ tự duyệt cây.
    public Guid Id { get; set; } // Id nút.
    public string Content { get; set; } = string.Empty; // Nội dung.
    public DateTime CreatedAt { get; set; } // Thời tạo.
    public Guid UserId { get; set; } // Tác giả.
    public Guid? ParentId { get; set; } // Cha.
    public bool IsResolved { get; set; } // Đã xử lý.
    public bool IsPinned { get; set; } // Ghim.
    public int Level { get; set; } // Độ sâu giữ nguyên từ cây CTE.
} // Kết thúc FeedbackFlattenCteDto.

public class CreateFeedbackDto
{
    public string Content { get; set; } = string.Empty; // Nội dung bắt buộc.
    public bool IsResolved { get; set; } // Trạng thái xử lý ban đầu.
    public bool IsPinned { get; set; } // Ghim.
    public Guid UserId { get; set; } // Tác giả.
    public Guid? ParentId { get; set; } // Trả lời feedback khác.
}

public class UpdateFeedbackDto
{
    public string Content { get; set; } = string.Empty; // Sửa nội dung.
    public bool IsResolved { get; set; } // Cập nhật đã xử lý.
    public bool IsPinned { get; set; } // Cập nhật ghim.
}

// Admin: chỉnh mọi trường có trên bảng feedback kể cả UserId, ParentId — service chặn gán cha tạo chu trình.
public class AdminUpdateFeedbackDto
{
    public string Content { get; set; } = string.Empty; // Nội dung sau chỉnh.
    public bool IsResolved { get; set; } // Cờ đã xử lý.
    public bool IsPinned { get; set; } // Ghim hiển thị.
    public Guid UserId { get; set; } // Tác giả — null-coalescing validator: không cho Empty (00000000...).
    public Guid? ParentId { get; set; } // null = gốc cây; khác null = cha phải tồn tại và không tạo vòng với subtree nút đang sửa.
}

#endregion

#region Post — bài đăng / thông báo

public class PostDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public Guid? ApartmentId { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreatePostDto
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public Guid? ApartmentId { get; set; }
    public bool IsPublished { get; set; } = true;
}

public class UpdatePostDto
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Guid? ApartmentId { get; set; }
    public bool IsPublished { get; set; }
}

#endregion

#region Attachment — tệp đính kèm

public class AttachmentDto
{
    public Guid Id { get; set; } // Khóa file.
    public AttachmentScope Scope { get; set; } // Phạm vi: avatar, feedback hoặc bài đăng.
    public string OriginalFileName { get; set; } = string.Empty; // Tên gốc client.
    public string StoredFileName { get; set; } = string.Empty; // Tên lưu trên đĩa.
    public string FilePath { get; set; } = string.Empty; // Đường dẫn tương đối/tuyệt đối máy chủ.
    public string ContentType { get; set; } = string.Empty; // MIME.
    public long FileSize { get; set; } // Kích thước byte.
    public string? FileHash { get; set; } // Hash kiểm toàn (nếu có).
    public Guid? UserId { get; set; } // Chủ sở hữu (nếu scope user).
    public Guid? FeedbackId { get; set; } // Gắn feedback.
    public Guid? PostId { get; set; } // Gắn bài đăng (Scope = Post).
    public DateTime CreatedAt { get; set; } // Upload lúc nào.
}

#endregion

#region RefreshToken — làm mới phiên

public class RefreshTokenDto
{
    public Guid Id { get; set; } // Id bản ghi token.
    public DateTime ExpiresAt { get; set; } // Hết hạn dùng.
    public bool IsRevoked { get; set; } // Đã thu hồi.
    public DateTime? RevokedAt { get; set; } // Thời điểm thu hồi.
    public Guid UserId { get; set; } // User sở hữu.
    public string? DeviceInfo { get; set; } // Thiết bị / client.
    public string? IpAddress { get; set; } // IP lúc cấp.
    public DateTime CreatedAt { get; set; } // Tạo token.
}

public class CreateRefreshTokenDto
{
    public string TokenHash { get; set; } = string.Empty; // Hash refresh (không lưu raw).
    public DateTime ExpiresAt { get; set; } // Hạn.
    public bool IsRevoked { get; set; } // Trạng thái ban đầu.
    public Guid UserId { get; set; } // User.
    public string? ReplacedByTokenHash { get; set; } // Luân chuyển token (rotate).
    public string? DeviceInfo { get; set; } // Thiết bị.
    public string? IpAddress { get; set; } // IP.
}

public class UpdateRefreshTokenDto
{
    public bool IsRevoked { get; set; } // Đánh dấu thu hồi.
    public DateTime? RevokedAt { get; set; } // Khi nào thu hồi.
    public string? ReplacedByTokenHash { get; set; } // Token thay thế.
}

#endregion

#region User / Role — Identity

public class UserListDto
{
    public Guid Id { get; set; } // User Id.
    public string? UserName { get; set; } // Đăng nhập.
    public string? Email { get; set; } // Email.
    public string FullName { get; set; } = string.Empty; // Tên hiển thị.
    public string? AvatarUrl { get; set; } // Ảnh đại diện.
    public bool IsActive { get; set; } // Khóa / mở tài khoản.
    public DateTime CreatedAt { get; set; } // Ngày tạo hồ sơ.
}

public class CreateUserDto
{
    public string UserName { get; set; } = string.Empty; // Tên đăng nhập.
    public string Email { get; set; } = string.Empty; // Email.
    public string Password { get; set; } = string.Empty; // Mật khẩu thô (chỉ đến service, không trả lại).
    public string FullName { get; set; } = string.Empty; // Họ tên.
}

public class UpdateUserDto
{
    public string FullName { get; set; } = string.Empty; // Cập nhật tên.
    public string? AvatarUrl { get; set; } // Ảnh.
    public bool IsActive { get; set; } = true; // Trạng thái hoạt động.
}

public class RoleDto
{
    public Guid Id { get; set; } // Id vai trò.
    public string? Name { get; set; } // Tên role (Admin, User…).
    public string? Description { get; set; } // Mô tả.
}

public class CreateRoleDto
{
    public string Name { get; set; } = string.Empty; // Tên role mới.
    public string? Description { get; set; } // Mô tả.
}

public class UpdateRoleDto
{
    public string? Description { get; set; } // Chỉ sửa mô tả (tên thường cố định sau tạo).
}

#endregion
