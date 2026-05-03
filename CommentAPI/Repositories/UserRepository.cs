using CommentAPI.Entities; 
using CommentAPI.Interfaces;
using CommentAPI.Data; 
using CommentAPI.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Repositories;

public class UserRepository : RepositoryBase<User>, IUserRepository // Truy vấn bảng Users và role join.
{
    #region Trường & hàm tạo

    public UserRepository(AppDbContext context) // Inject context.
        : base(context)
    {
    }

    #endregion

    #region Route Functions

    /// <summary>
    /// [1] Route: GET /api/users
    /// </summary>
    public async Task<(List<UserPageRow> Items, long TotalCount)> GetPagedAsync( // Phân trang + projection nhẹ.
        int page, // Page 1-based.
        int pageSize, // Size.
        CancellationToken cancellationToken = default, // CT.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null, // Lọc CreatedAt.
        string? nameContains = null, // Filter Name.
        string? userNameContains = null, // Filter UserName.
        string? emailContains = null) // Filter Email.
    {
        // Chuẩn hóa Skip/Take; Select chỉ cột cần cho DTO (không đọc PasswordHash, token…).
        var (p, s) = PaginationQuery.Normalize(page, pageSize); // Clamp page/size.
        var q = WhereCreatedAtRange(Context.Users.AsNoTracking(), createdAtFrom, createdAtTo); // Base + khoảng thời gian.
        var n = nameContains?.Trim();
        if (!string.IsNullOrEmpty(n))
            q = q.Where(u => u.Name.Contains(n)); // Name LIKE.
        var uN = userNameContains?.Trim();
        if (!string.IsNullOrEmpty(uN))
            q = q.Where(u => u.UserName != null && u.UserName.Contains(uN)); // UserName LIKE.
        var e = emailContains?.Trim();
        if (!string.IsNullOrEmpty(e))
            q = q.Where(u => u.Email != null && u.Email.Contains(e)); // Email LIKE.
        var total = await q.LongCountAsync(cancellationToken); // Count khớp lọc.
        var items = await q // Page query.
            .OrderBy(u => u.CreatedAt) // Primary sort.
            .ThenBy(u => u.Id) // Tie-breaker PK.
            .Skip((p - 1) * s) // Offset.
            .Take(s) // Limit.
            .Select(u => new UserPageRow( // Project to record.
                u.Id, // Id.
                u.Name, // Name.
                u.UserName ?? "", // Non-null string in row.
                u.Email, // Email.
                u.CreatedAt)) // Created.
            .ToListAsync(cancellationToken); // Execute.
        return (items, total); // Tuple result.
    }

    /// <summary>
    /// [1] Route: GET /api/users (batch roles)
    /// </summary>
    public async Task<Dictionary<Guid, List<string>>> GetRoleNamesByUserIdsAsync( // Map userId → roles.
        IReadOnlyList<Guid> userIds, // Input ids.
        CancellationToken cancellationToken = default) // CT.
    {
        if (userIds.Count == 0) // Short circuit empty.
        {
            return new Dictionary<Guid, List<string>>(); // Empty map.
        }

        // Một lần join AspNetUserRoles + AspNetRoles thay cho N lần UserManager.GetRolesAsync.
        var rows = await ( // LINQ join.
            from ur in Context.UserRoles.AsNoTracking() // User-role link.
            join r in Context.Roles.AsNoTracking() on ur.RoleId equals r.Id // Role master.
            where userIds.Contains(ur.UserId) // Filter relevant users.
            select new { ur.UserId, RoleName = r.Name } // Pair.
        ).ToListAsync(cancellationToken); // Materialize.

        return rows // Post-process in memory.
            .GroupBy(x => x.UserId) // Group by user.
            .ToDictionary( // To dictionary.
                g => g.Key, // User id key.
                g => g.Select(x => x.RoleName).Where(n => n != null).Cast<string>().OrderBy(n => n).ToList()); // Sorted role names.
    }

    #endregion

    #region Helpers

    // Lọc CreatedAt inclusive trên User.
    private static IQueryable<User> WhereCreatedAtRange(IQueryable<User> query, DateTime? createdAtFrom, DateTime? createdAtTo)
    {
        if (createdAtFrom is { } f)
            query = query.Where(u => u.CreatedAt >= f);
        if (createdAtTo is { } t)
            query = query.Where(u => u.CreatedAt <= t);
        return query;
    }

    public async Task<List<User>> GetAllAsync() // Toàn bộ users (ít dùng trong API hiện tại).
    {
        return await Context.Users // DbSet User.
            .AsNoTracking() // Read-only.
            .OrderBy(x => x.CreatedAt) // Sort stable by creation.
            .ToListAsync(); // Materialize.
    }

    #endregion
}
