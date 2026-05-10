using CommentAPI; // ApiException, ApiErrorCodes, ApiMessages (sort cột không hợp lệ).
using CommentAPI.DTOs; // PostDto — cột sort whitelist khớp JSON trả về.
using CommentAPI.Entities; // Post — lớp partial kết nối với PostRepository chính.

namespace CommentAPI.Repositories;

// Phần partial PostRepository: parse sort query + OrderBy an toàn theo whitelist PostDto.
public partial class PostRepository
{ // Mở partial PostRepository (sorting).
    // Mặc định danh sách post: CreatedAt giảm, ThenBy Id ổn định khi trùng thời gian.
    public static readonly SortByColumn PostListSortDefault = new("CreatedAt", true);

    // BƯỚC 1 — Parse hướng sort (sortDir) — sai format thì ApiException (InvalidSortDirection).
    // BƯỚC 2 — sort rỗng → trả PostListSortDefault; không rỗng → SortByColumn(trim, desc).
    public SortByColumn ParsePostListSortOrThrow(string? sort, string? sortDir)
    { // Mở ParsePostListSortOrThrow.
        var desc = SortByColumn.ParseDescendingOrThrow(sortDir); // asc/desc hoặc ascending/descending.
        if (string.IsNullOrWhiteSpace(sort))
            return PostListSortDefault; // Không gửi cột → mặc định nghiệp vụ.
        return new SortByColumn(sort.Trim(), desc); // Cột + hướng từ client.
    } // Kết thúc ParsePostListSortOrThrow.

    // BƯỚC 1 — Chuẩn hóa tên cột (trim + lower invariant) để switch case-insensitive.
    // BƯỚC 2 — map id/title/content/createdat/userid → OrderBy/ThenBy Id; cột lạ → ApiException PostInvalidSort.
    public IOrderedQueryable<PostDto> ApplyUniversalSorting(IQueryable<PostDto> query, SortByColumn spec)
    { // Mở ApplyUniversalSorting cho pipeline đã Select PostDto.
        var k = spec.Column.Trim().ToLowerInvariant(); // Khóa so khớp whitelist.
        var desc = spec.Descending; // true = giảm dần.
        return k switch
        {
            "id" => desc ? query.OrderByDescending(p => p.Id) : query.OrderBy(p => p.Id),
            "title" => desc
                ? query.OrderByDescending(p => p.Title).ThenBy(p => p.Id)
                : query.OrderBy(p => p.Title).ThenBy(p => p.Id),
            "content" => desc
                ? query.OrderByDescending(p => p.Content).ThenBy(p => p.Id)
                : query.OrderBy(p => p.Content).ThenBy(p => p.Id),
            "createdat" => desc
                ? query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id)
                : query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id),
            "userid" => desc
                ? query.OrderByDescending(p => p.UserId).ThenBy(p => p.Id)
                : query.OrderBy(p => p.UserId).ThenBy(p => p.Id),
            _ => throw new ApiException(400, ApiErrorCodes.PostInvalidSort, ApiMessages.PostInvalidSort),
        };
    } // Kết thúc ApplyUniversalSorting.
} // Kết thúc partial PostRepository (sorting).
