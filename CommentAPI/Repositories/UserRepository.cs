using CommentAPI; // SortByColumn — tham số sort phân trang user.
using CommentAPI.Entities; // User entity Identity.
using CommentAPI.Interfaces; // IUserRepository.
using CommentAPI.Data; // AppDbContext, DbSet Users/Roles/UserRoles.
using CommentAPI.DTOs; // UserPageRow projection.
using Microsoft.EntityFrameworkCore; // AsNoTracking, ToListAsync, v.v.

namespace CommentAPI.Repositories;

// =============================================================================
// File UserRepository.cs (partial): GET phân trang UserPageRow + batch roles; kế thừa RepositoryBase<User>.
// =============================================================================

// Truy vấn Users + batch role names — dùng cho GET /api/users và service gắn Roles vào UserDto.
public partial class UserRepository : RepositoryBase<User>, IUserRepository
{
    #region Trường & hàm tạo

    public UserRepository(AppDbContext context)
        : base(context)
    { // Mở khối constructor UserRepository.
        // BƯỚC 1 — Base(context) gán Context protected — không cần trường riêng.
    } // Kết thúc constructor UserRepository.

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
        string? emailContains = null,
        SortByColumn? sort = null)
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

        // BƯỚC 7: ApplyUniversalSorting rồi một trang projection UserPageRow.
        var spec = sort ?? UserListSortDefault;
        var ordered = ApplyUniversalSorting(q, spec);
        var items = await ordered
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
    { // Mở khối GetAllAsync.
        // BƯỚC 1 — Nạp toàn bộ user read-only theo CreatedAt — cẩn thận bộ nhớ khi bảng lớn.
        return await Context.Users
            .AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();
    } // Kết thúc GetAllAsync.

    #endregion
} // Kết thúc partial UserRepository.
