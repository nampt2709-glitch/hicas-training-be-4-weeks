using CommentAPI.Entities; // User : IdentityUser.
using CommentAPI.Interfaces; // IAuthenticationRepository contract.
using Microsoft.AspNetCore.Identity; // UserManager — login, stamp, roles.

namespace CommentAPI.Repositories;

// Repository xác thực: bọc UserManager<User> — không truy cập DbContext trực tiếp; Identity lo phần lưu trữ.
public class AuthenticationRepository : IAuthenticationRepository
{
    #region Trường & hàm tạo

    // UserManager cung cấp FindByName, CheckPassword, GetRoles, SecurityStamp, UpdateSecurityStamp — đăng ký qua AddIdentity.
    private readonly UserManager<User> _userManager;

    // BƯỚC 1: Tiêm UserManager từ container ASP.NET Core Identity.
    public AuthenticationRepository(UserManager<User> userManager)
    {
        _userManager = userManager; // Giữ tham chiếu cho toàn bộ phương thức route bên dưới.
    }

    #endregion

    #region Route Functions

    // [2] POST /api/auth/login — tra user theo UserName để kiểm tra mật khẩu sau đó.
    // TRƯỜNG HỢP: Không tồn tại user → null (service sẽ trả 401 giống mật khẩu sai).
    public Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken = default) =>
        _userManager.FindByNameAsync(userName); // Identity normalize userName theo cấu hình (uppercase store, v.v.).

    // [3]/[4] Refresh / Logout — tải user theo Guid (Id trong DB Identity là string trong store, convert ToString).
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _userManager.FindByIdAsync(id.ToString()); // FindByIdAsync nhận string key.

    // [2] Login — so khớp mật khẩu plaintext với hash trong DB (UserManager dùng IPasswordHasher).
    public async Task<bool> ValidatePasswordAsync(User user, string password, CancellationToken cancellationToken = default) =>
        await _userManager.CheckPasswordAsync(user, password); // true = đúng mật khẩu; false = sai.

    // [1]/[2]/[3] Lấy danh sách tên role của user — phục vụ gắn claim role vào access token.
    public async Task<IReadOnlyList<string>> GetRoleNamesAsync(User user, CancellationToken cancellationToken = default)
    {
        var roles = await _userManager.GetRolesAsync(user); // Một truy vấn join AspNetUserRoles + AspNetRoles.
        return roles.ToList(); // Materialize thành List<string> cố định để caller không phụ thuộc ICollection live.
    }

    // [3] Refresh — đọc security stamp hiện tại để so với claim trong refresh token; null coi thành chuỗi rỗng.
    public async Task<string> GetSecurityStampAsync(User user, CancellationToken cancellationToken = default)
    {
        return (await _userManager.GetSecurityStampAsync(user).ConfigureAwait(false)) ?? string.Empty; // ConfigureAwait(false): không bắt sync context; ?? tránh null reference khi so sánh chuỗi.
    }

    // [4] Logout — xoay security stamp: mọi refresh token cũ (claim stamp cũ) sẽ không còn hợp lệ.
    public async Task RevokeSessionsAsync(User user, CancellationToken cancellationToken = default)
    {
        await _userManager.UpdateSecurityStampAsync(user); // Cập nhật bản ghi user + giá trị stamp mới ngẫu nhiên.
    }

    #endregion
}
