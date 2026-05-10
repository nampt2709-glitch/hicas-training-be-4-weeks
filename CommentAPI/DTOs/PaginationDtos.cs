// Phân trang: response có metadata (trang, tổng, tổng số trang) và helper chuẩn hóa page/pageSize từ query.
using System.Globalization; // InvariantCulture khi format thông báo PageSizeExceedsMax.
using System.Text.Json.Serialization; // JsonIgnoreCondition — ẩn TotalCommentsInDb khi null.
using CommentAPI; // ApiMessages cho lỗi pageSize quá lớn.
using Microsoft.AspNetCore.Http; // StatusCodes cho ApiException trong PaginationQuery.

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

    // Mẫu số cho totalPages: route phẳng (1 item = 1 comment) = tổng comment; route CTE theo gốc = tổng gốc; route cây phẳng (phân trang theo từng dòng comment) = tổng comment.
    public long TotalCount { get; init; }

    // Tổng comment trong DB khớp lọc — chỉ gán khi route cần đối chiếu với phân trang theo gốc/subtree (vd. CTE, cây lồng); route phẳng thuần: null (dùng totalCount).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalComments { get; init; }

    // Tổng gốc cây (ParentId null) hoặc số gốc CTE — chỉ gán khi route có cây/lồng; route phẳng 1 dòng = 1 comment: null.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalNodes { get; init; }

    // Tổng số trang = ceil(TotalCount / PageSize); PageSize=0 thì 0 (tránh chia cho 0 ở client).
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

// Helper gán metadata comment: TotalCount luôn là mẫu số đúng cho TotalPages theo từng kiểu route.
public static class CommentPagedResult
{
    // Route trả danh sách phẳng: mỗi phần tử items = một comment → totalCount = tổng comment (mẫu số totalPages); không gán totalComments/totalNodes (tránh trùng ý nghĩa “node”).
    public static PagedResult<T> ForFlatCommentList<T>(List<T> items, int page, int pageSize, long totalCommentsMatchingFilter) // Một comment một dòng.
    {
        return new PagedResult<T> // Gói thống nhất.
        {
            Items = items, // Trang hiện tại.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = totalCommentsMatchingFilter, // totalPages = ceil(TotalCount / PageSize); cùng số phần tử logic với từng dòng response.
            TotalComments = null, // Không dùng trên route phẳng — trùng totalCount, gây hiểu nhầm “node”.
            TotalNodes = null // Không có khái niệm gốc cây trên danh sách phẳng thuần.
        };
    }

    // Route CTE phân trang theo gốc (cây lồng trong từng item hoặc flatten sau subtree): TotalPages theo TotalNodes; TotalComments = tổng bản ghi bảng khớp lọc.
    public static PagedResult<T> ForCtePagedByRootNodes<T>(List<T> items, int page, int pageSize, long totalCommentsInTable, long totalRootNodesInCte) // Gốc từ rừng CTE.
    {
        return new PagedResult<T> // Gói phân trang theo gốc.
        {
            Items = items, // Trang (đã flatten hoặc cây theo gốc trang).
            Page = page, // Trang.
            PageSize = pageSize, // Số gốc mỗi trang (ý nghĩa nghiệp vụ CTE).
            TotalCount = totalRootNodesInCte, // TotalPages theo số gốc.
            TotalComments = totalCommentsInTable, // Đối chiếu với COUNT bảng.
            TotalNodes = totalRootNodesInCte // Số gốc trong kết quả CTE.
        };
    }

    // Danh sách phẳng (mỗi dòng một comment) nhưng metadata kèm tổng gốc cây: TotalPages theo tổng comment; TotalNodes = tổng gốc (ParentId null) khớp lọc.
    public static PagedResult<T> ForFlatCommentPageWithRootTotals<T>(List<T> items, int page, int pageSize, long totalComments, long totalRootNodesInTable) // Phân trang theo comment, kèm đếm gốc.
    {
        return new PagedResult<T> // Gói metadata đầy đủ.
        {
            Items = items, // Trang.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang phẳng.
            TotalCount = totalComments, // TotalPages theo comment.
            TotalComments = totalComments, // Tổng comment khớp lọc.
            TotalNodes = totalRootNodesInTable // Gốc trong bảng (ParentId null).
        };
    }
}

// Chuẩn hóa page/pageSize (tối thiểu 1, tối đa MaxPageSize); ParseFromQuery: pageSize là số hợp lệ và > Max → ApiException 400; còn lại fallback + Normalize.
public static class PaginationQuery
{

    // Khi client không gửi pageSize, dùng 20 dòng mỗi trang mặc định.
    public const int DefaultPageSize = 20;

    // Trần bảo vệ: không cho pageSize lớn quá, tránh quét toàn bảng vô tình.
    public const int MaxPageSize = 500;

    // Cắt biên: trang ≥1; pageSize<1 thì dùng DefaultPageSize, >Max thì cắt về Max — trả tuple cho Skip/Take.
    public static (int Page, int PageSize) Normalize(int page, int pageSize)
    {
        var p = page < 1 ? 1 : page; // Trang tối thiểu 1, tránh offset âm ở repository.
        var s = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize); // Nếu pageSize 0/âm, lấy mặc định; nếu quá lớn, cắt ở Max.
        return (p, s); // Tuple truyền tới service/repository tính Skip/Take.
    }

    // Đọc chuỗi query: pageSize gửi rõ là số nguyên và vượt MaxPageSize → 400; trống/không phải số → fallback rồi Normalize.
    public static (int Page, int PageSize) ParseFromQuery(string? pageRaw, string? pageSizeRaw)
    {
        // Nếu client gửi pageSize cụ thể (parse được) mà lớn hơn trần — báo lỗi rõ, không im lặng cắt.
        if (!string.IsNullOrWhiteSpace(pageSizeRaw) // Có chuỗi pageSize (kể cả chỉ khoảng trắng đã trim ở bước TryParse).
            && int.TryParse(pageSizeRaw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var explicitSize) // Parse invariant giống ParseIntLoose.
            && explicitSize > MaxPageSize) // Vượt trần bảo vệ API.
        {
            throw new ApiException( // Middleware chuẩn hóa JSON lỗi.
                StatusCodes.Status400BadRequest, // 400: tham số query không hợp lệ.
                ApiErrorCodes.PageSizeTooLarge, // Mã ổn định cho client.
                string.Format(CultureInfo.InvariantCulture, ApiMessages.PageSizeExceedsMax, MaxPageSize)); // Thông điệp có nhúng max.
        } // Kết thúc nhánh pageSize quá lớn.

        var page = ParseIntLoose(pageRaw, 1); // Parse số trang, fallback 1, không ném ngoại lệ lên ngoài.
        var pageSize = ParseIntLoose(pageSizeRaw, DefaultPageSize); // Parse cỡ trang, rỗng thì 20, rồi sắp Normalize bên dưới.
        return Normalize(page, pageSize); // Cắt biên sau khi đã có số; coi mọi chuỗi lỗi thành fallback.
    }

    // paginationEnabled=false: tắt phân trang (chỉ dùng trên route demo). true: dùng page/pageSize.
    public static (bool Unpaged, int Page, int PageSize) ParsePaginationFromQuery(
        string? pageRaw,
        string? pageSizeRaw,
        bool paginationEnabled)
    {
        if (!paginationEnabled) // Client tắt phân trang — không Skip/Take ở tầng service.
        {
            return (true, 1, DefaultPageSize); // Tuple hợp lệ; Unpaged=true bỏ qua p/s khi đọc DB.
        }

        var (p, s) = ParseFromQuery(pageRaw, pageSizeRaw); // Bật phân trang — chuẩn hóa trang/cỡ.
        return (false, p, s); // Cắt trang theo p/s.
    }

    private static int ParseIntLoose(string? raw, int fallback) // Hàm phụ, không công khai, parse linh hoạt, không throw.
    {
        if (string.IsNullOrWhiteSpace(raw)) // Bỏ qua trắng hoặc thiếu, trả mặc định ủy nhiệm từ caller.
        {
            return fallback; // Dùng giá trị mặc định 1 cho page hoặc DefaultPageSize cho size.
        }

        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) // Parse invariant để 123 không bị môi trường lôi theo dấu phẩy.
            ? v // Nếu thành công, trả số thực tế, có thể vẫn 0/âm → Normalize sẽ xử.
            : fallback; // Không phải số nguyên, im lặng dùng fallback, không 400 vì sản phẩm phân trang mềm.
    }
}
