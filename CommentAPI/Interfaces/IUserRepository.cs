using CommentAPI.DTOs;
using CommentAPI.Entities;

// Hợp đồng truy cập dữ liệu user: tách EF khỏi dịch vụ, hỗ trợ kiểm thử thay thế.
namespace CommentAPI.Interfaces;

// Tầng truy vấn: danh sách, phân trang có projection, tìm, gom role theo nhiều id, CRUD, lưu.
public interface IUserRepository
{
    Task<List<User>> GetAllAsync(); // Đọc toàn bộ user thành entity (thường ít dùng sản xuất, debug/ admin).

    // Phân trang: trả tuple (danh sách dòng projection, tổng số bản ghi); name/userName/email là filter Contains tuỳ chọn.
    Task<(List<UserPageRow> Items, long TotalCount)> GetPagedAsync(
        int page, // Số trang, dùng Skip/Take trong SQL.
        int pageSize, // Cỡ trang, giới hạn truy vấn.
        CancellationToken cancellationToken = default, // Hủy truy vấn dài.
        DateTime? createdAtFrom = null, // Lọc CreatedAt inclusive.
        DateTime? createdAtTo = null, // Lọc CreatedAt inclusive.
        string? nameContains = null, // Chuỗi con trong Name.
        string? userNameContains = null, // Chuỗi con trong UserName.
        string? emailContains = null); // Chuỗi con trong Email.

    // Một truy vấn lấy mọi role theo danh sách user id, tránh N+1 lần gọi GetRoles mỗi user.
    Task<Dictionary<Guid, List<string>>> GetRoleNamesByUserIdsAsync(
        IReadOnlyList<Guid> userIds, // Danh sách id duy nhất cần role, — rỗng thì trả từ điển rỗng.
        CancellationToken cancellationToken = default); // Hủy khi join lớn.

    Task<User?> GetByIdAsync(Guid id); // Một entity theo khóa, null nếu không có, có thể AsNoTracking.
    Task AddAsync(User user); // Đánh dấu thêm, SaveChanges gọi riêng.
    void Update(User user); // Đánh dấu sửa entity đã track.
    void Remove(User user); // Đánh dấu xóa.
    Task<bool> ExistsAsync(Guid id); // Kiểm tra tồn tại bằng Any, không cần nạp đủ hàng.
    Task SaveChangesAsync(); // Ghi thay đổi xuống SQL Server một lần.
}
