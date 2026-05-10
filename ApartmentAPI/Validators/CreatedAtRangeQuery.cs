// Kiểm tra cặp createdAtFrom / createdAtTo từ query (inclusive).
using ApartmentAPI; // ApiException.
using Microsoft.AspNetCore.Http; // StatusCodes.

namespace ApartmentAPI.Validators;

// Helper throw ApiException khi khoảng ngày CreatedAt vô lý (from > to).
public static class CreatedAtRangeQuery
{ // Mở khối CreatedAtRangeQuery.
    // Cả hai boundary đều có và from > to → 400 MODEL_VALIDATION_FAILED.
    public static void ValidateOrThrow(DateTime? createdAtFrom, DateTime? createdAtTo)
    { // Mở khối ValidateOrThrow.
        // TRƯỜNG HỢP A — Cả hai null hoặc chỉ một bên null → hợp lệ (không ràng buộc).
        if (createdAtFrom is { } f && createdAtTo is { } t && f > t)
        {
            // TRƯỜNG HỢP B — from lớn hơn to: không thể lọc inclusive.
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ModelValidationFailed,
                ApiMessages.CreatedAtRangeInvalid);
        }
    } // Kết thúc ValidateOrThrow.
} // Kết thúc CreatedAtRangeQuery.
