using System.Globalization;

namespace CommentAPI.DTOs;

/// <summary>
/// Hằng số và chuẩn hóa phân trang cho query <c>page</c> / <c>pageSize</c>.
/// Dùng <see cref="ParseFromQuery"/> với <c>string?</c> để <c>page=</c> / thiếu / không phải số vẫn về mặc định 1 và 20.
/// </summary>
public static class PaginationQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Đảm bảo trang ≥ 1 và kích thước trong [1, MaxPageSize].</summary>
    public static (int Page, int PageSize) Normalize(int page, int pageSize)
    {
        var p = page < 1 ? 1 : page;
        var s = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        return (p, s);
    }

    /// <summary>
    /// Đọc <c>page</c> / <c>pageSize</c> từ query: null, rỗng, hoặc không parse được int → dùng 1 và <see cref="DefaultPageSize"/> trước khi <see cref="Normalize"/>.
    /// </summary>
    public static (int Page, int PageSize) ParseFromQuery(string? pageRaw, string? pageSizeRaw)
    {
        var page = ParseIntLoose(pageRaw, 1);
        var pageSize = ParseIntLoose(pageSizeRaw, DefaultPageSize);
        return Normalize(page, pageSize);
    }

    private static int ParseIntLoose(string? raw, int fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : fallback;
    }
}
