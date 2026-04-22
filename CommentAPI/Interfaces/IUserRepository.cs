using CommentAPI.DTOs;
using CommentAPI.Entities;

// Hợp đồng truy cập dữ liệu user: tách EF khỏi dịch vụ, hỗ trợ kiểm thử thay thế.
namespace CommentAPI.Interfaces;

// Tầng truy vấn: danh sách, phân trang có projection, tìm, gom role theo nhiều id, CRUD, lưu.
public interface IUserRepository
{
    Task<List<User>> GetAllAsync(); // Đọc toàn bộ user thành entity (thường ít dùng sản xuất, debug/ admin).

    // Phân trang: trả tuple (danh sách dòng projection, tổng số bản ghi); không tải PasswordHash.
    Task<(List<UserPageRow> Items, long TotalCount)> GetPagedAsync(
        int page, // Số trang, dùng Skip/Take trong SQL.
        int pageSize, // Cỡ trang, giới hạn truy vấn.
        CancellationToken cancellationToken = default); // Hủy truy vấn dài.

    // Tìm user có Name chứa chuỗi, phân trang; dùng Contains/ LIKE tùy provider.
    Task<(List<UserPageRow> Items, long TotalCount)> SearchByNamePagedAsync(
        string nameContains, // Chuỗi con tìm trong Name, không null ở caller nếu đã chuẩn hóa.
        int page, // Trang phân trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default); // Hủy.

    // Tìm user có UserName chứa chuỗi, phân trang.
    Task<(List<UserPageRow> Items, long TotalCount)> SearchByUserNamePagedAsync(
        string userNameContains, // Chuỗi con trong UserName.
        int page, // Trang.
        int pageSize, // Cỡ.
        CancellationToken cancellationToken = default); // Hủy.

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
