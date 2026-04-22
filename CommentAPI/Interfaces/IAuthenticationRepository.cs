using CommentAPI.Entities; // Thực thể User, — truy cập tầng bảng người dùng Identity.

// Hợp đồng tầng truy vấn auth, — tách khỏi UserManager, — test có thể mô phỏng lớp này.
namespace CommentAPI.Interfaces;

// Giao ước: đọc user, mật khẩu, role, dấu bảo mật, hủy phiên, — tất cả async, trừ mật thật từ client không tồn tại ở entity.
public interface IAuthenticationRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default); // Tìm user theo Guid, null nếu không có, — không tải toàn tập bảng.
    Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken = default); // Tìm theo tên đăng nhập, phân biệt hoa thường tùy cấp Identity, — dùng login.
    Task<bool> ValidatePasswordAsync(User user, string password, CancellationToken cancellationToken = default); // So khớp mật thuân với băm, — tận dụng dịch vụ mật khẩu Identity, — bên trong mới tới DB/ store.
    Task<IReadOnlyList<string>> GetRoleNamesAsync(User user, CancellationToken cancellationToken = default); // Danh tên role dạng chuỗi, dùng phát hành claim role trong JWT.
    Task<string> GetSecurityStampAsync(User user, CancellationToken cancellationToken = default); // Lấy security stamp, — đóng vào claim, — kiểm soát sau khi user đổi mật/ khóa.
    Task RevokeSessionsAsync(User user, CancellationToken cancellationToken = default); // Vô hiệu refresh, — tùy lưu trữ, — khi đăng xuất/ đổi mật tùy chính sách.
}
