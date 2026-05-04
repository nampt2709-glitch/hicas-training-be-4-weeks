using CommentAPI.Entities;
using CommentAPI.Interfaces;
using CommentAPI.Data;
using CommentAPI.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Repositories;

// Truy vấn Users + batch role names — dùng cho GET /api/users và service gắn Roles vào UserDto.
public class UserRepository : RepositoryBase<User>, IUserRepository
{
    #region Trường & hàm tạo

    public UserRepository(AppDbContext context)
        : base(context)
    {
    }

    #endregion

    #region Route Functions

    // [1] GET /api/users — phân trang UserPageRow + TotalCount; repo tự Normalize page/pageSize.
    public async Task<(List<UserPageRow> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        string? nameContains = null,
        string? userNameContains = null,
        string? emailContains = null)
    {
        // BƯỚC 1: Chuẩn hóa page/size (clamp tối thiểu 1) — tránh Skip âm hoặc Take 0.
        var (p, s) = PaginationQuery.Normalize(page, pageSize);

        // BƯỚC 2: Bắt đầu từ Users AsNoTracking + lọc CreatedAt qua base helper.
        var q = ApplyCreatedAtRange(Context.Users.AsNoTracking(), createdAtFrom, createdAtTo);

        // BƯỚC 3: Lọc Name Contains nếu có chuỗi hợp lệ sau trim.
        var n = nameContains?.Trim();
        if (!string.IsNullOrEmpty(n))
            q = q.Where(u => u.Name.Contains(n));

        // BƯỚC 4: UserName — kiểm tra null vì cột có thể null tùy dữ liệu Identity.
        var uN = userNameContains?.Trim();
        if (!string.IsNullOrEmpty(uN))
            q = q.Where(u => u.UserName != null && u.UserName.Contains(uN));

        // BƯỚC 5: Email tương tự UserName.
        var e = emailContains?.Trim();
        if (!string.IsNullOrEmpty(e))
            q = q.Where(u => u.Email != null && u.Email.Contains(e));

        // BƯỚC 6: COUNT(*) khớp lọc — metadata phân trang.
        var total = await q.LongCountAsync(cancellationToken);

        // BƯỚC 7: Một trang projection nhẹ UserPageRow — không Select PasswordHash.
        var items = await q
            .OrderBy(u => u.CreatedAt) // Cũ đến mới (khác PostRepository — theo convention user list).
            .ThenBy(u => u.Id)
            .Skip((p - 1) * s)
            .Take(s)
            .Select(u => new UserPageRow(
                u.Id,
                u.Name,
                u.UserName ?? "",
                u.Email,
                u.CreatedAt))
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    // [1] GET /api/users — batch: một query trả map UserId → danh sách tên role (tránh N+1 GetRolesAsync).
    public async Task<Dictionary<Guid, List<string>>> GetRoleNamesByUserIdsAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        // TRƯỜNG HỢP A: Danh sách Id rỗng — không cần SQL.
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, List<string>>(); // Dictionary rỗng, không null.
        }

        // BƯỚC 1: Join UserRoles với Roles, chỉ các UserId nằm trong tập truyền vào.
        var rows = await (
            from ur in Context.UserRoles.AsNoTracking()
            join r in Context.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where userIds.Contains(ur.UserId)
            select new { ur.UserId, RoleName = r.Name }
        ).ToListAsync(cancellationToken);

        // BƯỚC 2: Group theo UserId trong RAM — OrderBy role name để output ổn định.
        return rows
            .GroupBy(x => x.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.RoleName).Where(n => n != null).Cast<string>().OrderBy(n => n).ToList());
    }

    #endregion

    #region Helpers

    // Toàn bộ user entity read-only — dùng ít; cẩn thận bộ nhớ khi bảng lớn.
    public async Task<List<User>> GetAllAsync()
    {
        return await Context.Users
            .AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();
    }

    #endregion
}
