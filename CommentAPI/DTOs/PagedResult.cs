// DTOs phân trang: response JSON có metadata (trang, tổng, tổng số trang tính được).
namespace CommentAPI.DTOs;

// Một trang dữ liệu: danh sách T + số thứ tự trang + kích thước trang + tổng bản ghi toàn tập kết quả.
public class PagedResult<T>
{
    // Phần tử trang hiện tại; List để thống nhất với cache JSON và serializer.
    public List<T> Items { get; init; } = new();

    // Số trang 1-based sau khi Normalize (tối thiểu 1).
    public int Page { get; init; }

    // Số bản ghi tối đa mỗi trang, đã cắt ở MaxPageSize ở tầng gọi.
    public int PageSize { get; init; }

    // Tổng số bản ghi khớp truy vấn (trước khi phân trang cắt).
    public long TotalCount { get; init; }

    // Tổng số trang = ceil(TotalCount / PageSize); PageSize=0 thì 0 (tránh chia cho 0 ở client).
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
