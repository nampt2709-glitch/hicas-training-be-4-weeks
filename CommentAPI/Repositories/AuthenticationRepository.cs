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

    #region Route Functions

    /// <summary>
    /// [2] Route: POST /api/auth/login
    /// </summary>
    public Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken = default) =>
        _userManager.FindByNameAsync(userName);

    /// <summary>
    /// [3]/[4] Route helper: POST /api/auth/refresh, POST /api/auth/logout
    /// </summary>
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _userManager.FindByIdAsync(id.ToString());

    /// <summary>
    /// [2] Route: POST /api/auth/login
    /// </summary>
    public async Task<bool> ValidatePasswordAsync(User user, string password, CancellationToken cancellationToken = default) =>
        await _userManager.CheckPasswordAsync(user, password);

    /// <summary>
    /// [1]/[2]/[3] Route helper: resolve role claims for token
    /// </summary>
    public async Task<IReadOnlyList<string>> GetRoleNamesAsync(User user, CancellationToken cancellationToken = default)
    {
        var roles = await _userManager.GetRolesAsync(user); // Gọi store role
        return roles.ToList(); // Materialize thành list cố định
    }

    /// <summary>
    /// [3] Route: POST /api/auth/refresh
    /// </summary>
    public async Task<string> GetSecurityStampAsync(User user, CancellationToken cancellationToken = default)
    {
        return (await _userManager.GetSecurityStampAsync(user).ConfigureAwait(false)) ?? string.Empty; // Null thành rỗng
    }

    /// <summary>
    /// [4] Route: POST /api/auth/logout
    /// </summary>
    public async Task RevokeSessionsAsync(User user, CancellationToken cancellationToken = default)
    {
        await _userManager.UpdateSecurityStampAsync(user); // Tạo stamp mới trên bản ghi
    }

    #endregion
}
