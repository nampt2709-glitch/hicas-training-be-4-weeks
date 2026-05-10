using CommentAPI; // ApiException, ApiErrorCodes, ApiMessages.
using CommentAPI.DTOs; // CommentDto / projection dùng khi sort map cache key.
using CommentAPI.Entities; // Comment entity — tên cột whitelist sort.

namespace CommentAPI.Repositories;

// Phần sort comment: whitelist cột theo từng shape (entity / CTE / DTO demo / cây CTE), parse query, và khóa cache biến thể sort.
public partial class CommentRepository
{
    // Mặc định list phân trang phẳng / gốc / CTE (PostId → CreatedAt → Id tăng) — thay cho enum route cũ ByPostCreatedAtId.
    public static readonly SortByColumn CommentListSortDefault = new("PostId", false);

    // Mặc định unpaged flat theo thời gian tăng (thay ByCreatedAt).
    public static readonly SortByColumn CommentFlatUnpagedSortDefault = new("CreatedAt", false);

    // Đọc sortDir an toàn: null = asc; asc/ascending vs desc/descending.
    public static bool ParseSortDirectionOrThrow(string? sortDir) =>
        SortByColumn.ParseDescendingOrThrow(sortDir);

    // Parse sort + sortDir: null sort → default list; legacy tên enum cũ hoặc số 0..4 (client vẫn có thể gửi).
    public SortByColumn ParseCommentListSortOrThrow(string? sort, string? sortDir, bool unpagedFlatDefaultsToCreatedAt = false)
    { // Mở khối ParseCommentListSortOrThrow — chuẩn hóa sort query cho mọi route comment list.
        // BƯỚC 1 — Parse hướng sortDir (null = asc; asc/desc/… hoặc throw ApiException).
        var desc = ParseSortDirectionOrThrow(sortDir);

        // BƯỚC 2 — sort rỗng → default list hoặc default flat unpaged tùy cờ unpagedFlatDefaultsToCreatedAt.
        if (string.IsNullOrWhiteSpace(sort))
        {
            return unpagedFlatDefaultsToCreatedAt
                ? CommentFlatUnpagedSortDefault
                : CommentListSortDefault;
        }

        // BƯỚC 3 — Legacy: tên enum/string + số 0..4 — hướng cố định trong mapping (bỏ qua sortDir).
        var t = sort.Trim();

        // Legacy — hướng đã nằm trong tên / mã, bỏ qua sortDir.
        if (t.Equals("ByCreatedAt", StringComparison.OrdinalIgnoreCase) || t == "0")
            return new SortByColumn("CreatedAt", false);
        if (t.Equals("ByPostCreatedAtId", StringComparison.OrdinalIgnoreCase) || t == "1")
            return CommentListSortDefault;
        if (t.Equals("ByCreatedAtDesc", StringComparison.OrdinalIgnoreCase) || t == "2")
            return new SortByColumn("CreatedAt", true);
        if (t.Equals("ByUserIdCreatedAtId", StringComparison.OrdinalIgnoreCase) || t == "3")
            return new SortByColumn("UserId", false);
        if (t.Equals("ByIdAsc", StringComparison.OrdinalIgnoreCase) || t == "4")
            return new SortByColumn("Id", false);

        // Cột tự do: sortDir điều khiển hướng.
        return new SortByColumn(t, desc);
    } // Kết thúc ParseCommentListSortOrThrow.

    // Mọi cột CTE × asc/desc — dùng xóa cache resource GET /api/posts/{id}/comments/* sau CRUD.
    public static IEnumerable<SortByColumn> EnumerateCommentCteSortSpecsForCache()
    { // Mở khối EnumerateCommentCteSortSpecsForCache — sinh mọi biến thể sort whitelist cho khóa cache CTE.
        foreach (var col in new[] { "Id", "Content", "CreatedAt", "PostId", "UserId", "ParentId", "Level" })
        {
            yield return new SortByColumn(col, false);
            yield return new SortByColumn(col, true);
        }
    } // Kết thúc EnumerateCommentCteSortSpecsForCache.

    // LINQ IQueryable<Comment>: các cột entity (không có Level).
    public IOrderedQueryable<Comment> ApplyUniversalSorting(IQueryable<Comment> query, SortByColumn spec) =>
        ApplyCommentEntitySort(query, NormalizeSortKey(spec.Column), spec.Descending);

    // LINQ trên dòng CTE (sau materialize hoặc EF).
    public IOrderedQueryable<CommentCteDto> ApplyUniversalSorting(IQueryable<CommentCteDto> query, SortByColumn spec) =>
        ApplyCommentCteSort(query, NormalizeSortKey(spec.Column), spec.Descending);

    // Demo projection / eager map: đủ cột trả về CommentLoadingDemoDto.
    public IOrderedQueryable<CommentLoadingDemoDto> ApplyUniversalSorting(
        IQueryable<CommentLoadingDemoDto> query,
        SortByColumn spec)
    {
        var k = NormalizeSortKey(spec.Column);
        var desc = spec.Descending;
        return k switch
        {
            "commentid" or "id" => desc
                ? query.OrderByDescending(x => x.CommentId)
                : query.OrderBy(x => x.CommentId),
            "loadingstrategy" => desc
                ? query.OrderByDescending(x => x.LoadingStrategy).ThenBy(x => x.CommentId)
                : query.OrderBy(x => x.LoadingStrategy).ThenBy(x => x.CommentId),
            "content" => desc
                ? query.OrderByDescending(x => x.Content).ThenBy(x => x.CommentId)
                : query.OrderBy(x => x.Content).ThenBy(x => x.CommentId),
            "createdat" => desc
                ? query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.CommentId)
                : query.OrderBy(x => x.CreatedAt).ThenBy(x => x.CommentId),
            "postid" => desc
                ? query.OrderByDescending(x => x.PostId).ThenBy(x => x.CreatedAt).ThenBy(x => x.CommentId)
                : query.OrderBy(x => x.PostId).ThenBy(x => x.CreatedAt).ThenBy(x => x.CommentId),
            "posttitle" => desc
                ? query.OrderByDescending(x => x.PostTitle).ThenBy(x => x.CommentId)
                : query.OrderBy(x => x.PostTitle).ThenBy(x => x.CommentId),
            "userid" => desc
                ? query.OrderByDescending(x => x.UserId).ThenBy(x => x.CreatedAt).ThenBy(x => x.CommentId)
                : query.OrderBy(x => x.UserId).ThenBy(x => x.CreatedAt).ThenBy(x => x.CommentId),
            "authorusername" => desc
                ? query.OrderByDescending(x => x.AuthorUserName).ThenBy(x => x.CommentId)
                : query.OrderBy(x => x.AuthorUserName).ThenBy(x => x.CommentId),
            "parentid" => desc
                ? query.OrderByDescending(x => x.ParentId).ThenBy(x => x.CommentId)
                : query.OrderBy(x => x.ParentId).ThenBy(x => x.CommentId),
            "childrencount" => desc
                ? query.OrderByDescending(x => x.ChildrenCount).ThenBy(x => x.CommentId)
                : query.OrderBy(x => x.ChildrenCount).ThenBy(x => x.CommentId),
            _ => throw new ApiException(400, ApiErrorCodes.CommentInvalidSort, ApiMessages.CommentInvalidSort),
        };
    }

    // Sắp danh sách gốc cây CTE trong RAM (service phân trang gốc).
    public static List<CommentTreeCteDto> SortCommentTreeCteRootsForPaging(
        IEnumerable<CommentTreeCteDto> roots,
        SortByColumn spec) =>
        ApplyCommentTreeCteSort(roots.AsQueryable(), NormalizeSortKey(spec.Column), spec.Descending).ToList();

    public IOrderedQueryable<CommentTreeCteDto> ApplyUniversalSorting(
        IQueryable<CommentTreeCteDto> query,
        SortByColumn spec) =>
        ApplyCommentTreeCteSort(query, NormalizeSortKey(spec.Column), spec.Descending);

    // Chuẩn hóa tên cột trước switch whitelist.
    private static string NormalizeSortKey(string column)
    {
        if (string.IsNullOrWhiteSpace(column))
            throw new ApiException(400, ApiErrorCodes.CommentInvalidSort, ApiMessages.CommentInvalidSort);
        var t = column.Trim();
        return t.ToLowerInvariant() switch
        {
            "commentid" => "id",
            _ => t.ToLowerInvariant(),
        };
    }

    private static IOrderedQueryable<Comment> ApplyCommentEntitySort(IQueryable<Comment> q, string k, bool desc)
    {
        if (k is "level" or "loadingstrategy" or "posttitle" or "authorusername" or "childrencount")
            throw new ApiException(400, ApiErrorCodes.CommentInvalidSort, ApiMessages.CommentInvalidSort);

        if (k == "postid")
        {
            return desc
                ? q.OrderByDescending(c => c.PostId).ThenByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
                : q.OrderBy(c => c.PostId).ThenBy(c => c.CreatedAt).ThenBy(c => c.Id);
        }

        if (k is "id" or "commentid")
            return desc ? q.OrderByDescending(c => c.Id) : q.OrderBy(c => c.Id);

        return k switch
        {
            "content" => desc
                ? q.OrderByDescending(c => c.Content).ThenBy(c => c.Id)
                : q.OrderBy(c => c.Content).ThenBy(c => c.Id),
            "createdat" => desc
                ? q.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
                : q.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id),
            "userid" => desc
                ? q.OrderByDescending(c => c.UserId).ThenBy(c => c.CreatedAt).ThenBy(c => c.Id)
                : q.OrderBy(c => c.UserId).ThenBy(c => c.CreatedAt).ThenBy(c => c.Id),
            "parentid" => desc
                ? q.OrderByDescending(c => c.ParentId).ThenBy(c => c.Id)
                : q.OrderBy(c => c.ParentId).ThenBy(c => c.Id),
            _ => throw new ApiException(400, ApiErrorCodes.CommentInvalidSort, ApiMessages.CommentInvalidSort),
        };
    }

    private static IOrderedQueryable<CommentCteDto> ApplyCommentCteSort(IQueryable<CommentCteDto> q, string k, bool desc)
    {
        if (k == "postid")
        {
            return desc
                ? q.OrderByDescending(x => x.PostId).ThenByDescending(x => x.Level).ThenByDescending(x => x.CreatedAt)
                    .ThenByDescending(x => x.Id)
                : q.OrderBy(x => x.PostId).ThenBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id);
        }

        return k switch
        {
            "id" => desc ? q.OrderByDescending(x => x.Id) : q.OrderBy(x => x.Id),
            "content" => desc
                ? q.OrderByDescending(x => x.Content).ThenBy(x => x.Id)
                : q.OrderBy(x => x.Content).ThenBy(x => x.Id),
            "createdat" => desc
                ? q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                : q.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            "userid" => desc
                ? q.OrderByDescending(x => x.UserId).ThenBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id)
                : q.OrderBy(x => x.UserId).ThenBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id),
            "parentid" => desc
                ? q.OrderByDescending(x => x.ParentId).ThenBy(x => x.Id)
                : q.OrderBy(x => x.ParentId).ThenBy(x => x.Id),
            "level" => desc
                ? q.OrderByDescending(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id)
                : q.OrderBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id),
            _ => throw new ApiException(400, ApiErrorCodes.CommentInvalidSort, ApiMessages.CommentInvalidSort),
        };
    }

    private static IOrderedQueryable<CommentTreeCteDto> ApplyCommentTreeCteSort(
        IQueryable<CommentTreeCteDto> q,
        string k,
        bool desc)
    {
        if (k == "postid")
        {
            return desc
                ? q.OrderByDescending(x => x.PostId).ThenByDescending(x => x.Level).ThenByDescending(x => x.CreatedAt)
                    .ThenByDescending(x => x.Id)
                : q.OrderBy(x => x.PostId).ThenBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id);
        }

        return k switch
        {
            "id" => desc ? q.OrderByDescending(x => x.Id) : q.OrderBy(x => x.Id),
            "content" => desc
                ? q.OrderByDescending(x => x.Content).ThenBy(x => x.Id)
                : q.OrderBy(x => x.Content).ThenBy(x => x.Id),
            "createdat" => desc
                ? q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                : q.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            "userid" => desc
                ? q.OrderByDescending(x => x.UserId).ThenBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id)
                : q.OrderBy(x => x.UserId).ThenBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id),
            "parentid" => desc
                ? q.OrderByDescending(x => x.ParentId).ThenBy(x => x.Id)
                : q.OrderBy(x => x.ParentId).ThenBy(x => x.Id),
            "level" => desc
                ? q.OrderByDescending(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id)
                : q.OrderBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id),
            _ => throw new ApiException(400, ApiErrorCodes.CommentInvalidSort, ApiMessages.CommentInvalidSort),
        };
    }
}
