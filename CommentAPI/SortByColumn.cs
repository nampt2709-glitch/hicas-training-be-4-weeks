namespace CommentAPI;

// =============================================================================
// File SortByColumn.cs: struct record gói (tên cột + hướng desc) — parse sortDir, sinh đoạn khóa cache.
// =============================================================================

// Nó có nhiệm vụ gói input từ client sau khi parse (sort + sortDir → tên cột + asc/desc), đồng thời có CacheKeySegment / ParseDescendingOrThrow để dùng chung trên controller → service → repository và cache.
// Nó trả lời câu “Người dùng muốn sắp theo cột nào, chiều nào?”.

// Tham số sắp xếp theo một cột JSON response + hướng; dùng chung parse/ cache segment (không gắn enum theo route).
public readonly record struct SortByColumn(string Column, bool Descending)
{
    // Chuẩn hóa tên cột cho switch whitelist (PascalCase hoặc camelCase từ query đều map cùng key).
    public string NormalizedKey => Column.Trim(); // Trim khoảng đầu/cuối.

    // Đoạn nhúng khóa cache distributed: cột thường + hướng (tránh snapshot sai khi đổi sort).
    public string CacheKeySegment =>
        $"{NormalizedKey.ToLowerInvariant().Replace(':', '_')}-{(Descending ? "d" : "a")}"; // Tránh ':' trong segment khóa.

    // Đọc sortDir query: null = asc; asc vs desc (400 nếu không nhận diện).
    public static bool ParseDescendingOrThrow(string? sortDir)
    { // Mở khối ParseDescendingOrThrow.
        // BƯỚC 1 — sortDir trống → ascending mặc định.
        if (string.IsNullOrWhiteSpace(sortDir))
            return false;

        // BƯỚC 2 — So khớp asc / ascending (OrdinalIgnoreCase).
        var t = sortDir.Trim();
        if (t.Equals("asc", StringComparison.OrdinalIgnoreCase)
            || t.Equals("ascending", StringComparison.OrdinalIgnoreCase))
            return false;

        // BƯỚC 3 — So khớp desc / descending.
        if (t.Equals("desc", StringComparison.OrdinalIgnoreCase)
            || t.Equals("descending", StringComparison.OrdinalIgnoreCase))
            return true;

        // BƯỚC 4 — Giá trị không hiểu → ApiException 400 InvalidSortDirection.
        throw new ApiException(
            400,
            ApiErrorCodes.InvalidSortDirection,
            ApiMessages.InvalidSortDirection);
    } // Kết thúc ParseDescendingOrThrow.
} // Kết thúc struct SortByColumn.
