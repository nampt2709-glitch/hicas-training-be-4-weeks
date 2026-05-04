using CommentAPI; // ApiException, ApiErrorCodes, ApiMessages.
using Microsoft.AspNetCore.Http;

namespace CommentAPI.Validators;

// Chuẩn hóa query `sort` cho mọi route danh sách comment — dropdown client gửi tên enum hoặc số 0..4.
public static class CommentSortQuery
{
    // Bỏ qua null/blank → mặc định ByPostCreatedAtId; sai định dạng → ApiException 400.
    public static CommentRouteListSort ParseOrThrow(string? sort)
    { // Mở khối ParseOrThrow.
        // BƯỚC 1 — Không gửi sort: giữ hành vi cũ (PostId → CreatedAt → Id).
        if (string.IsNullOrWhiteSpace(sort)) // Không có hoặc chỉ khoảng trắng.
            return CommentRouteListSort.ByPostCreatedAtId; // Mặc định thống nhất list phẳng / gốc.

        var t = sort.Trim(); // Chuỗi sau trim.

        // BƯỚC 2 — Thử parse theo tên enum (không phân biệt hoa thường).
        if (Enum.TryParse<CommentRouteListSort>(t, ignoreCase: true, out var byName) && Enum.IsDefined(typeof(CommentRouteListSort), byName)) // Tên hợp lệ.
            return byName; // Trả giá trị enum.

        // BƯỚC 3 — Thử parse số nguyên (client dropdown value = index).
        if (int.TryParse(t, out var i) && Enum.IsDefined(typeof(CommentRouteListSort), i)) // 0..4 hợp lệ.
            return (CommentRouteListSort)i; // Ép kiểu an toàn sau IsDefined.

        // BƯỚC 4 — Không nhận dạng được → 400 nghiệp vụ.
        throw new ApiException( // Lỗi đầu vào sort.
            StatusCodes.Status400BadRequest, // HTTP 400.
            ApiErrorCodes.CommentInvalidSort, // Mã ổn định cho client.
            ApiMessages.CommentInvalidSort); // Thông điệp hướng dẫn giá trị cho phép.
    } // Kết thúc ParseOrThrow.
}
