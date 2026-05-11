using System.Globalization; // Parse số trang invariant.
using System.Text.Json.Serialization; // Ẩn field null trên JSON.
using ApartmentAPI; // ApiException, ApiErrorCodes, ApiMessages.
using Microsoft.AspNetCore.Http; // StatusCodes 400.

namespace ApartmentAPI.DTOs;

// Một trang dữ liệu: Items + metadata trang (totalPages suy từ TotalCount / PageSize).
public class PagedResult<T>
{ // Mở khối PagedResult — envelope danh sách phân trang.
    public List<T> Items { get; init; } = new(); // Các phần tử trang hiện tại.
    public int Page { get; init; } // Số trang (1-based trong Normalize).
    public int PageSize { get; init; } // Kích thước trang sau clamp.
    public long TotalCount { get; init; } // Tổng bản ghi khớp lọc (hoặc gốc cây tùy API).

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalComments { get; init; } // Metadata tuỳ chọn (Comment-style lists).

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalNodes { get; init; } // Metadata tuỳ chọn (cây / nodes).

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize); // Số trang làm tròn lên.
} // Kết thúc PagedResult.

// Gói metadata cho route feedback CTE — cùng quy ước TotalCount/TotalComments/TotalNodes như CommentPagedResult (hệ CommentAPI).
public static class FeedbackPagedResult
{ // Mở khối FeedbackPagedResult — factory PagedResult cho GET /feedbacks/cte và tree/cte*.
    // Mỗi phần tử trang = một dòng CTE phẳng; TotalPages theo tổng số dòng CTE sau lọc.
    public static PagedResult<T> ForFlatFeedbackCteList<T>(List<T> items, int page, int pageSize, long totalFlatRowsMatchingCte) // Một nút một dòng.
    { // Mở khối ForFlatFeedbackCteList.
        return new PagedResult<T> // Shape giống CommentPagedResult.ForFlatCommentList.
        {
            Items = items, // Trang hiện tại.
            Page = page, // Trang 1-based.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = totalFlatRowsMatchingCte, // Mẫu số TotalPages.
            TotalComments = null, // Không dùng trên route phẳng thuần.
            TotalNodes = null,
        };
    } // Kết thúc ForFlatFeedbackCteList.

    // Phân trang theo số gốc cây trong RAM; TotalCount = tổng gốc CTE; TotalComments = COUNT bảng khớp lọc.
    public static PagedResult<T> ForCtePagedByRootNodes<T>(List<T> items, int page, int pageSize, long totalFeedbacksInTable, long totalRootNodesInCte) // Gốc mỗi item có thể là subtree.
    { // Mở khối ForCtePagedByRootNodes.
        return new PagedResult<T>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalRootNodesInCte,
            TotalComments = totalFeedbacksInTable,
            TotalNodes = totalRootNodesInCte,
        };
    } // Kết thúc ForCtePagedByRootNodes.
} // Kết thúc FeedbackPagedResult.

// Chuẩn hóa page/pageSize từ query; pageSize vượt Max → ApiException 400 + PAGE_SIZE_TOO_LARGE.
public static class PaginationQuery
{ // Mở khối PaginationQuery.
    public const int DefaultPageSize = 20; // Khi client không gửi pageSize.
    public const int MaxPageSize = 500; // Trần an toàn chống query quá lớn.

    // Clamp page ≥1 và pageSize vào [1, MaxPageSize] hoặc default khi <1.
    public static (int Page, int PageSize) Normalize(int page, int pageSize)
    { // Mở khối Normalize.
        var p = page < 1 ? 1 : page; // Trang tối thiểu 1.
        var s = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize); // pageSize hợp lệ.
        return (p, s);
    } // Kết thúc Normalize.

    // Parse chuỗi query: pageSize explicit > Max → throw; còn lại parse lỏng rồi Normalize.
    public static (int Page, int PageSize) ParseFromQuery(string? pageRaw, string? pageSizeRaw)
    { // Mở khối ParseFromQuery.
        // TRƯỜNG HỢP A — Client gửi pageSize cụ thể vượt Max: báo lỗi rõ ràng (không silent clamp).
        if (!string.IsNullOrWhiteSpace(pageSizeRaw)
            && int.TryParse(pageSizeRaw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var explicitSize)
            && explicitSize > MaxPageSize)
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.PageSizeTooLarge,
                string.Format(CultureInfo.InvariantCulture, ApiMessages.PageSizeExceedsMax, MaxPageSize));
        }

        // BƯỚC 1 — Parse page và pageSize lỏng (không hợp lệ → fallback).
        var page = ParseIntLoose(pageRaw, 1);
        var pageSize = ParseIntLoose(pageSizeRaw, DefaultPageSize);
        // BƯỚC 2 — Normalize clamp.
        return Normalize(page, pageSize);
    } // Kết thúc ParseFromQuery.

    // Parse int invariant; null/invalid → fallback.
    private static int ParseIntLoose(string? raw, int fallback)
    { // Mở khối ParseIntLoose.
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : fallback;
    } // Kết thúc ParseIntLoose.
} // Kết thúc PaginationQuery.
