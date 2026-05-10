using ApartmentAPI.Entities; // BaseEntity: CreatedAt dùng cho lọc khoảng thời gian.
using Microsoft.EntityFrameworkCore; // EF Core: IQueryable.Where.

namespace ApartmentAPI.Repositories;

// Extension lọc CreatedAt(from/to) tái sử dụng cho mọi entity kế thừa BaseEntity (đồng bộ pattern CommentAPI).
public static class BaseEntityQueryExtensions
{ // Mở khối BaseEntityQueryExtensions.
    // Thu hẹp IQueryable theo khoảng CreatedAt — mỗi tham số tùy chọn, bỏ qua nếu null.
    public static IQueryable<T> WhereCreatedAtRange<T>(
        this IQueryable<T> query, // Chuỗi LINQ đầu vào (chưa thực thi SQL).
        DateTime? createdAtFrom, // Ngưỡng dưới (bao gồm) hoặc null.
        DateTime? createdAtTo) // Ngưỡng trên (bao gồm) hoặc null.
        where T : BaseEntity // Ràng buộc: có cột CreatedAt mapping.
    { // Mở khối WhereCreatedAtRange.
        // BƯỚC 1 — Nếu có createdAtFrom thì thêm điều kiện CreatedAt >= f.
        if (createdAtFrom is { } f) // Pattern matching: chỉ vào khi có giá trị.
            query = query.Where(e => e.CreatedAt >= f); // Lọc từ mốc from.

        // BƯỚC 2 — Nếu có createdAtTo thì thêm điều kiện CreatedAt <= t.
        if (createdAtTo is { } t) // Pattern matching: chỉ vào khi có giá trị.
            query = query.Where(e => e.CreatedAt <= t); // Lọc đến mốc to.

        // TRƯỜNG HỢP: Cả hai null — trả nguyên query (không thêm điều kiện thời gian).
        return query; // IQueryable đã chèn 0–2 predicate CreatedAt.
    } // Kết thúc WhereCreatedAtRange.
} // Kết thúc BaseEntityQueryExtensions.
