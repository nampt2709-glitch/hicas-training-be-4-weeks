// Kiểm tra khoảng lọc CreatedAt từ query (inclusive) — dùng chung cho GET có cột CreatedAt.
using CommentAPI;
using Microsoft.AspNetCore.Http;

namespace CommentAPI.Validators;

// Tiện ích tĩnh: không lưu trạng thái; chỉ ValidateOrThrow cho cặp from/to.
public static class CreatedAtRangeQuery
{
    // Nếu cả hai có giá trị mà from > to → 400; ngược lại không làm gì (một hoặc không có biên vẫn hợp lệ).
    public static void ValidateOrThrow(DateTime? createdAtFrom, DateTime? createdAtTo) // Hai biên tuỳ chọn từ query string.
    { // Mở khối ValidateOrThrow.
        if (createdAtFrom is { } f && createdAtTo is { } t && f > t) // Khoảng rỗng theo thời gian.
        { // Mở khối lỗi.
            throw new ApiException( // Chuẩn hóa JSON lỗi qua middleware.
                StatusCodes.Status400BadRequest, // 400: tham số query không hợp lệ.
                ApiErrorCodes.ModelValidationFailed, // Mã ổn định.
                ApiMessages.CreatedAtRangeInvalid); // Thông điệp cho client.
        } // Kết thúc nhánh lỗi.
    } // Kết thúc ValidateOrThrow.
}
