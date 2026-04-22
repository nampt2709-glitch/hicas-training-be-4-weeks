using CommentAPI.Entities; 
using CommentAPI.Interfaces;
using CommentAPI.Data; 
using CommentAPI.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Repositories; 

public class UserRepository : IUserRepository // Truy vấn bảng Users và role join.
{
    private readonly AppDbContext _context; // DbContext scoped per request.

    public UserRepository(AppDbContext context) // Inject context.
    {
        _context = context; // Store.
    }

    public async Task<List<User>> GetAllAsync() // Toàn bộ users (ít dùng trong API hiện tại).
    {
        return await _context.Users // DbSet User.
            .AsNoTracking() // Read-only.
            .OrderBy(x => x.CreatedAt) // Sort stable by creation.
            .ToListAsync(); // Materialize.
    }

    public async Task<(List<UserPageRow> Items, long TotalCount)> GetPagedAsync( // Phân trang + projection nhẹ.
        int page, // Page 1-based.
        int pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        // Chuẩn hóa Skip/Take; Select chỉ cột cần cho DTO (không đọc PasswordHash, token…).
        var (p, s) = PaginationQuery.Normalize(page, pageSize); // Clamp page/size.
        var q = _context.Users.AsNoTracking(); // Base query.
        var total = await q.LongCountAsync(cancellationToken); // Count all.
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

    public async Task<(List<UserPageRow> Items, long TotalCount)> SearchByNamePagedAsync( // Filter Name contains.
        string nameContains, // Pattern.
        int page, // Page.
        int pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.Normalize(page, pageSize); // Normalize.
        var q = _context.Users.AsNoTracking().Where(u => u.Name.Contains(nameContains)); // Filter — SQL translation.
        var total = await q.LongCountAsync(cancellationToken); // Count filtered.
        var items = await q // Page.
            .OrderBy(u => u.CreatedAt) // Sort.
            .ThenBy(u => u.Id) // Tie.
            .Skip((p - 1) * s) // Skip.
            .Take(s) // Take.
            .Select(u => new UserPageRow( // Project.
                u.Id, // Id.
                u.Name, // Name.
                u.UserName ?? "", // Username.
                u.Email, // Email.
                u.CreatedAt)) // Created.
            .ToListAsync(cancellationToken); // List.
        return (items, total); // Out.
    }

    public async Task<(List<UserPageRow> Items, long TotalCount)> SearchByUserNamePagedAsync( // UserName contains.
        string userNameContains, // Pattern.
        int page, // Page.
        int pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.Normalize(page, pageSize); // Normalize.
        var q = _context.Users.AsNoTracking() // Base.
            .Where(u => u.UserName != null && u.UserName.Contains(userNameContains)); // Nullable guard.
        var total = await q.LongCountAsync(cancellationToken); // Count.
        var items = await q // Page.
            .OrderBy(u => u.CreatedAt) // Sort.
            .ThenBy(u => u.Id) // Tie.
            .Skip((p - 1) * s) // Skip.
            .Take(s) // Take.
            .Select(u => new UserPageRow( // Row.
                u.Id, // Id.
                u.Name, // Name.
                u.UserName ?? "", // Username.
                u.Email, // Email.
                u.CreatedAt)) // Created.
            .ToListAsync(cancellationToken); // Execute.
        return (items, total); // Return.
    }

    // Kế thừa hợp đồng: batch-load tên role theo danh sách user id (một truy vấn).
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
            from ur in _context.UserRoles.AsNoTracking() // User-role link.
            join r in _context.Roles.AsNoTracking() on ur.RoleId equals r.Id // Role master.
            where userIds.Contains(ur.UserId) // Filter relevant users.
            select new { ur.UserId, RoleName = r.Name } // Pair.
        ).ToListAsync(cancellationToken); // Materialize.

        return rows // Post-process in memory.
            .GroupBy(x => x.UserId) // Group by user.
            .ToDictionary( // To dictionary.
                g => g.Key, // User id key.
                g => g.Select(x => x.RoleName).Where(n => n != null).Cast<string>().OrderBy(n => n).ToList()); // Sorted role names.
    }

    public async Task<User?> GetByIdAsync(Guid id) // Single user by PK (tracked default FirstOrDefault).
    {
        return await _context.Users.FirstOrDefaultAsync(x => x.Id == id); // May return null.
    }

    public async Task AddAsync(User user) // Insert user entity.
    {
        await _context.Users.AddAsync(user); // Stage.
    }

    public void Update(User user) // Attach update.
    {
        _context.Users.Update(user); // Whole entity marked modified.
    }

    public void Remove(User user) // Delete user row.
    {
        _context.Users.Remove(user); // Stage delete.
    }

    public async Task<bool> ExistsAsync(Guid id) // Existence check cheap.
    {
        return await _context.Users.AsNoTracking().AnyAsync(x => x.Id == id); // Any.
    }

    public async Task SaveChangesAsync() // Flush unit of work.
    {
        await _context.SaveChangesAsync(); // EF save.
    }
}
