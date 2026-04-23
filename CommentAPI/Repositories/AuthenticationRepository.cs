using CommentAPI.Entities;
using CommentAPI.Interfaces;
using Microsoft.AspNetCore.Identity;


namespace CommentAPI.Repositories;

public class AuthenticationRepository : IAuthenticationRepository
{
    #region Trường & hàm tạo

    // Quản lý tạo user, băm mật khẩu, gán role, security stamp.
    private readonly UserManager<User> _userManager;

    // DI: UserManager đăng ký bởi AddIdentity ở Program.
    public AuthenticationRepository(UserManager<User> userManager)
    {
        _userManager = userManager; // Lưu tham chiếu
    }

    #endregion

    #region Đọc user — AuthController / SignUp / Login / Refresh / Logout

    // Tìm theo tên đăng nhập (UserName) — login.
    public Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken = default) =>
        _userManager.FindByNameAsync(userName);

    // Tìm user theo Guid (chuỗi) — hợp với bảng AspNetUsers.
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _userManager.FindByIdAsync(id.ToString());

    // Gọi CheckPasswordAsync: so khớp mật khẩu gửi lên với hash trong DB.
    public async Task<bool> ValidatePasswordAsync(User user, string password, CancellationToken cancellationToken = default) =>
        await _userManager.CheckPasswordAsync(user, password);

    // Tên role: AspNetUserRoles nối user–role, trả list chuỗi.
    public async Task<IReadOnlyList<string>> GetRoleNamesAsync(User user, CancellationToken cancellationToken = default)
    {
        var roles = await _userManager.GetRolesAsync(user); // Gọi store role
        return roles.ToList(); // Materialize thành list cố định
    }

    // Stamp hiện tại: so sánh với claim trong token khi refresh.
    public async Task<string> GetSecurityStampAsync(User user, CancellationToken cancellationToken = default)
    {
        return (await _userManager.GetSecurityStampAsync(user).ConfigureAwait(false)) ?? string.Empty; // Null thành rỗng
    }

    #endregion

    #region Phiên — logout / revoke

    // Đổi security stamp: vô hiệu mọi token/refresh cũ phát trước khi thay đổi.
    public async Task RevokeSessionsAsync(User user, CancellationToken cancellationToken = default)
    {
        await _userManager.UpdateSecurityStampAsync(user); // Tạo stamp mới trên bản ghi
    }

    #endregion
}
