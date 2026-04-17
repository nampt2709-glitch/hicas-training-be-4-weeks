using CommentAPI.Entities;
using CommentAPI.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace CommentAPI.Repositories;

// Lớp truy cập dữ liệu đăng nhập: bọc UserManager (Identity) để giữ logic trong repository.
public class AuthenticationRepository : IAuthenticationRepository
{
    private readonly UserManager<User> _userManager;

    public AuthenticationRepository(UserManager<User> userManager)
    {
        _userManager = userManager;
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _userManager.FindByIdAsync(id.ToString());

    public Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken = default) =>
        _userManager.FindByNameAsync(userName);

    public async Task<bool> ValidatePasswordAsync(User user, string password, CancellationToken cancellationToken = default) =>
        await _userManager.CheckPasswordAsync(user, password);

    public async Task<IReadOnlyList<string>> GetRoleNamesAsync(User user, CancellationToken cancellationToken = default)
    {
        var roles = await _userManager.GetRolesAsync(user);
        return roles.ToList();
    }

    public async Task<string> GetSecurityStampAsync(User user, CancellationToken cancellationToken = default)
    {
        return (await _userManager.GetSecurityStampAsync(user).ConfigureAwait(false)) ?? string.Empty;
    }

    public async Task RevokeSessionsAsync(User user, CancellationToken cancellationToken = default)
    {
        await _userManager.UpdateSecurityStampAsync(user);
    }
}
