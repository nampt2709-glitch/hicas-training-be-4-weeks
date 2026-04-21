namespace CommentAPI.DTOs;

/// <summary>
/// Một trang dữ liệu trả về cho client (metadata + danh sách phần tử).
/// </summary>
public class PagedResult<T>
{
    /// <summary>Danh sách phần tử trang hiện tại (kiểu danh sách để cache JSON round-trip ổn định).</summary>
    public List<T> Items { get; init; } = new();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long TotalCount { get; init; }

    /// <summary>Tổng số trang (làm tròn lên).</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
