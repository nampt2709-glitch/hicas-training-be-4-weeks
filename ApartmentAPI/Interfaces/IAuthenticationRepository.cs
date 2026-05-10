using ApartmentAPI.Entities; // User entity cho repository auth.

namespace ApartmentAPI.Interfaces;

// Truy cập dữ liệu Identity/User/phân quyền cho luồng đăng nhập — tách khỏi AuthenticationService.
public interface IAuthenticationRepository
{ // Mở khối IAuthenticationRepository.
    // Tìm user theo UserName (đăng nhập) — null nếu không tồn tại.
    Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken = default);
    // Tải user theo khóa chính Guid — dùng refresh/logout.
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    // So khớp mật khẩu với hash lưu DB (PasswordHasher/Identity) — async để không chặn thread pool nếu implementation nặng.
    Task<bool> ValidatePasswordAsync(User user, string password, CancellationToken cancellationToken = default);
    // Tên role của user — phát hành claim roles trong JWT.
    Task<IReadOnlyList<string>> GetRoleNamesAsync(User user, CancellationToken cancellationToken = default);
    // SecurityStamp hiện tại — invalidation token khi đổi mật khẩu/role (tuỳ policy).
    Task<string> GetSecurityStampAsync(User user, CancellationToken cancellationToken = default);
    // Thu hồi mọi refresh token (sessions) của user — logout toàn cục hoặc đổi mật khẩu.
    Task RevokeSessionsAsync(User user, CancellationToken cancellationToken = default);
} // Kết thúc IAuthenticationRepository.
