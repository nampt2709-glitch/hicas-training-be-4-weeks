using ApartmentAPI.V1.DTOs; // FeedbackCteDto — hàng SqlQueryRaw.
using Microsoft.EntityFrameworkCore; // SqlQueryRaw, ToListAsync.

namespace ApartmentAPI.Repositories;

// Partial FeedbackRepository: CTE đệ quy SQL Server (không PostId; lọc IsDeleted trong raw SQL vì SqlQueryRaw bỏ query filter).
public sealed partial class FeedbackRepository
{ // Mở khối partial FeedbackRepository — CTE + sort sau materialize.
    // CTE FeedbackTree: neo ParentId IS NULL; đệ quy nối f2.ParentId = ft.Id; Level tăng dần; chỉ bản ghi chưa xóa mềm.
    private const string FeedbackFullCteSql = """
WITH FeedbackTree AS (
    SELECT f.Id, f.Content, f.CreatedAt, f.ParentId, f.UserId, f.IsResolved, f.IsPinned, CAST(0 AS int) AS Level
    FROM Feedbacks AS f
    WHERE f.ParentId IS NULL AND f.IsDeleted = 0
    UNION ALL
    SELECT f2.Id, f2.Content, f2.CreatedAt, f2.ParentId, f2.UserId, f2.IsResolved, f2.IsPinned, ft.Level + 1
    FROM Feedbacks AS f2
    INNER JOIN FeedbackTree AS ft ON f2.ParentId = ft.Id
    WHERE f2.IsDeleted = 0
)
SELECT Id, Content, CreatedAt, ParentId, UserId, IsResolved, IsPinned, Level
FROM FeedbackTree
WHERE ({0} IS NULL OR CreatedAt >= {0}) AND ({1} IS NULL OR CreatedAt <= {1})
  AND ({2} IS NULL OR UserId = {2})
  AND ({3} IS NULL OR Content LIKE {3})
""";

    // COUNT(*) khớp lọc route — dùng làm TotalComments (metadata) giống CommentAPI CountCommentsMatchingRouteAsync.
    public async Task<long> CountFeedbacksMatchingRouteAsync(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        string? contentContains,
        CancellationToken ct = default)
    { // Mở khối CountFeedbacksMatchingRouteAsync.
        // BƯỚC 1 — Cùng predicate với GetPagedAsync khi rootsOnly = false (EF áp global soft delete).
        var q = Set.AsNoTracking().AsQueryable();
        q = q.WhereCreatedAtRange(createdAtFrom, createdAtTo);
        if (userId is { } uid)
            q = q.Where(f => f.UserId == uid);
        var txt = contentContains?.Trim();
        if (!string.IsNullOrEmpty(txt))
            q = q.Where(f => f.Content.Contains(txt));
        // BƯỚC 2 — Một round-trip COUNT.
        return await q.LongCountAsync(ct);
    } // Kết thúc CountFeedbacksMatchingRouteAsync.

    // Một câu SqlQueryRaw + ORDER BY an toàn trong RAM (whitelist FeedbackListSort).
    public async Task<List<FeedbackCteDto>> LoadRawCteAsync(
        CancellationToken ct = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        FeedbackListSort sort = default)
    { // Mở khối LoadRawCteAsync.
        // BƯỚC 1 — Hóa null thành DBNull để vô hiệu từng nhánh WHERE trong chuỗi SQL.
        object fromArg = createdAtFrom ?? (object)DBNull.Value;
        object toArg = createdAtTo ?? (object)DBNull.Value;
        object userArg = userId ?? (object)DBNull.Value;
        object likeArg = string.IsNullOrWhiteSpace(contentContains)
            ? DBNull.Value
            : (object)("%" + contentContains.Trim() + "%");
        // BƯỚC 2 — Materialize toàn bộ hàng CTE (service phân trang flat hoặc dựng cây trong RAM).
        var rows = await Context.Database
            .SqlQueryRaw<FeedbackCteDto>(FeedbackFullCteSql, fromArg, toArg, userArg, likeArg)
            .ToListAsync(ct);
        // BƯỚC 3 — Sắp theo cột sort an toàn (đồng bộ với SortFeedbackTreeCteRootsForPaging).
        return ApplyFeedbackCteMaterializedSort(rows, sort);
    } // Kết thúc LoadRawCteAsync.

    // Thực thi OrderBy whitelist trên tập đã nạp (không tin chuỗi sort từ client trực tiếp).
    private static List<FeedbackCteDto> ApplyFeedbackCteMaterializedSort(List<FeedbackCteDto> rows, FeedbackListSort spec) =>
        ApplyFeedbackCteSort(rows.AsQueryable(), spec).ToList();

    // Sắp danh sách gốc cây CTE trước Skip/Take phân trang theo thread (mirror SortCommentTreeCteRootsForPaging).
    public static List<FeedbackTreeCteDto> SortFeedbackTreeCteRootsForPaging(IEnumerable<FeedbackTreeCteDto> roots, FeedbackListSort spec) =>
        ApplyFeedbackTreeCteRootSort(roots.AsQueryable(), spec).ToList();

    // Map enum sort → IOrderedQueryable hàng CTE phẳng.
    private static IOrderedQueryable<FeedbackCteDto> ApplyFeedbackCteSort(IQueryable<FeedbackCteDto> q, FeedbackListSort spec)
    { // Mở khối ApplyFeedbackCteSort.
        var desc = spec.Descending;
        return spec.Column switch
        {
            FeedbackSortColumn.Id => desc ? q.OrderByDescending(x => x.Id) : q.OrderBy(x => x.Id),
            FeedbackSortColumn.Content => desc
                ? q.OrderByDescending(x => x.Content).ThenByDescending(x => x.Id)
                : q.OrderBy(x => x.Content).ThenBy(x => x.Id),
            FeedbackSortColumn.CreatedAt => desc
                ? q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                : q.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            FeedbackSortColumn.UserId => desc
                ? q.OrderByDescending(x => x.UserId).ThenByDescending(x => x.Level).ThenByDescending(x => x.CreatedAt)
                    .ThenByDescending(x => x.Id)
                : q.OrderBy(x => x.UserId).ThenBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id),
            FeedbackSortColumn.IsPinned => desc
                ? q.OrderByDescending(x => x.IsPinned).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                : q.OrderBy(x => x.IsPinned).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id),
            FeedbackSortColumn.IsResolved => desc
                ? q.OrderByDescending(x => x.IsResolved).ThenByDescending(x => x.Id)
                : q.OrderBy(x => x.IsResolved).ThenBy(x => x.Id),
            FeedbackSortColumn.ParentId => desc
                ? q.OrderByDescending(x => x.ParentId).ThenByDescending(x => x.Id)
                : q.OrderBy(x => x.ParentId).ThenBy(x => x.Id),
            FeedbackSortColumn.Level => desc
                ? q.OrderByDescending(x => x.Level).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                : q.OrderBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id),
            _ => q.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
        };
    } // Kết thúc ApplyFeedbackCteSort.

    // Cùng whitelist sort cho nút gốc (scalar giống FeedbackCteDto).
    private static IOrderedQueryable<FeedbackTreeCteDto> ApplyFeedbackTreeCteRootSort(IQueryable<FeedbackTreeCteDto> q, FeedbackListSort spec)
    { // Mở khối ApplyFeedbackTreeCteRootSort.
        var desc = spec.Descending;
        return spec.Column switch
        {
            FeedbackSortColumn.Id => desc ? q.OrderByDescending(x => x.Id) : q.OrderBy(x => x.Id),
            FeedbackSortColumn.Content => desc
                ? q.OrderByDescending(x => x.Content).ThenByDescending(x => x.Id)
                : q.OrderBy(x => x.Content).ThenBy(x => x.Id),
            FeedbackSortColumn.CreatedAt => desc
                ? q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                : q.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            FeedbackSortColumn.UserId => desc
                ? q.OrderByDescending(x => x.UserId).ThenByDescending(x => x.Level).ThenByDescending(x => x.CreatedAt)
                    .ThenByDescending(x => x.Id)
                : q.OrderBy(x => x.UserId).ThenBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id),
            FeedbackSortColumn.IsPinned => desc
                ? q.OrderByDescending(x => x.IsPinned).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                : q.OrderBy(x => x.IsPinned).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id),
            FeedbackSortColumn.IsResolved => desc
                ? q.OrderByDescending(x => x.IsResolved).ThenByDescending(x => x.Id)
                : q.OrderBy(x => x.IsResolved).ThenBy(x => x.Id),
            FeedbackSortColumn.ParentId => desc
                ? q.OrderByDescending(x => x.ParentId).ThenByDescending(x => x.Id)
                : q.OrderBy(x => x.ParentId).ThenBy(x => x.Id),
            FeedbackSortColumn.Level => desc
                ? q.OrderByDescending(x => x.Level).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                : q.OrderBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id),
            _ => q.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
        };
    } // Kết thúc ApplyFeedbackTreeCteRootSort.
} // Kết thúc partial FeedbackRepository (CTE).
