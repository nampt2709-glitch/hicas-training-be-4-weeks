using CommentAPI.DTOs;
using CommentAPI.Entities;

namespace CommentAPI.Interfaces;

public interface IUserRepository
{
    Task<List<User>> GetAllAsync();

    /// <summary>Phân trang user — projection, không tải PasswordHash.</summary>
    Task<(List<UserPageRow> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Tìm user có <see cref="User.Name"/> chứa chuỗi (phân trang).</summary>
    Task<(List<UserPageRow> Items, long TotalCount)> SearchByNamePagedAsync(
        string nameContains,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Tìm user có <see cref="User.UserName"/> chứa chuỗi (phân trang).</summary>
    Task<(List<UserPageRow> Items, long TotalCount)> SearchByUserNamePagedAsync(
        string userNameContains,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Một truy vấn: mọi role theo danh sách UserId (tránh N+1 GetRolesAsync).</summary>
    Task<Dictionary<Guid, List<string>>> GetRoleNamesByUserIdsAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken = default);

    Task<User?> GetByIdAsync(Guid id);
    Task AddAsync(User user);
    void Update(User user);
    void Remove(User user);
    Task<bool> ExistsAsync(Guid id);
    Task SaveChangesAsync();
}
