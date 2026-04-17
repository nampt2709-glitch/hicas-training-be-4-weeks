using CommentAPI.Entities;

namespace CommentAPI.Interfaces;

/// <summary>
/// Truy vấn người dùng và vai trò phục vụ đăng nhập (Identity UserManager).
/// </summary>
public interface IAuthenticationRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken = default);
    Task<bool> ValidatePasswordAsync(User user, string password, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetRoleNamesAsync(User user, CancellationToken cancellationToken = default);
    Task<string> GetSecurityStampAsync(User user, CancellationToken cancellationToken = default);
    Task RevokeSessionsAsync(User user, CancellationToken cancellationToken = default);
}
