using ApartmentAPI.Entities; // User entity Identity.
using ApartmentAPI.Interfaces; // IAuthenticationRepository.
using Microsoft.AspNetCore.Identity; // UserManager<TUser>.

namespace ApartmentAPI.Repositories;

// Repository xác thực: bọc UserManager để login / stamp / roles — không truy vấn DbContext trực tiếp.
public sealed class AuthenticationRepository : IAuthenticationRepository
{ // Mở khối AuthenticationRepository.
    // UserManager của ASP.NET Identity — truy vấn bảng Users qua store đã cấu hình.
    private readonly UserManager<User> _userManager; // Thao tác user, mật khẩu, security stamp.

    public AuthenticationRepository(UserManager<User> userManager) // Tiêm UserManager từ DI.
    { // Mở khối constructor AuthenticationRepository.
        // BƯỚC 1 — Lưu UserManager để mọi phương thức gọi API Identity nhất quán.
        _userManager = userManager; // Instance scoped theo HTTP/request tùy đăng ký.
    } // Kết thúc constructor AuthenticationRepository.

    // Tìm user theo UserName (normalized) — null nếu không có.
    public Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken = default) // userName: đăng nhập.
    { // Mở khối GetByUserNameAsync.
        // BƯỚC 1 — FindByNameAsync của Identity (bất đồng bộ tới user store).
        return _userManager.FindByNameAsync(userName); // cancellationToken không truyền vào API Identity mặc định.
    } // Kết thúc GetByUserNameAsync.

    // Tìm user theo Id (chuỗi trong Identity) — Guid.ToString().
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) // id: khóa Guid.
    { // Mở khối GetByIdAsync.
        // BƯỚC 1 — FindByIdAsync nhận string id.
        return _userManager.FindByIdAsync(id.ToString()); // Map Guid → string store.
    } // Kết thúc GetByIdAsync.

    // Xác thực mật khẩu (hash so trong UserManager) — true nếu đúng.
    public async Task<bool> ValidatePasswordAsync(User user, string password, CancellationToken cancellationToken = default) // user + plain password.
    { // Mở khối ValidatePasswordAsync.
        // BƯỚC 1 — CheckPasswordAsync: so hash theo PasswordHasher đã đăng ký.
        return await _userManager.CheckPasswordAsync(user, password); // Không lộ hash ra ngoài.
    } // Kết thúc ValidatePasswordAsync.

    // Lấy danh sách tên role (string) của user — materialize IReadOnlyList.
    public async Task<IReadOnlyList<string>> GetRoleNamesAsync(User user, CancellationToken cancellationToken = default) // user: chủ thể.
    { // Mở khối GetRoleNamesAsync.
        // BƯỚC 1 — GetRolesAsync trả IList<string> từ user-role store.
        var roles = await _userManager.GetRolesAsync(user); // Query join user-roles.

        // BƯỚC 2 — Copy sang List để giao diện IReadOnlyList cố định.
        return roles.ToList(); // Materialize mới — tránh mutate ngoài.
    } // Kết thúc GetRoleNamesAsync.

    // Lấy security stamp hiện tại — rỗng nếu null (phòng null reference ở JWT layer).
    public async Task<string> GetSecurityStampAsync(User user, CancellationToken cancellationToken = default) // user: entity.
    { // Mở khối GetSecurityStampAsync.
        // BƯỚC 1 — GetSecurityStampAsync + coalesce sang string.Empty.
        return (await _userManager.GetSecurityStampAsync(user).ConfigureAwait(false)) ?? string.Empty; // ConfigureAwait false: không bắt sync context.
    } // Kết thúc GetSecurityStampAsync.

    // Vô hiệu hóa phiên: đổi SecurityStamp → JWT refresh cũ fail.
    public async Task RevokeSessionsAsync(User user, CancellationToken cancellationToken = default) // user cần logout mọi thiết bị.
    { // Mở khối RevokeSessionsAsync.
        // BƯỚC 1 — UpdateSecurityStampAsync: sinh stamp mới và persist user.
        await _userManager.UpdateSecurityStampAsync(user); // Invalidate cookies/tokens phụ thuộc stamp.
    } // Kết thúc RevokeSessionsAsync.
} // Kết thúc AuthenticationRepository.
