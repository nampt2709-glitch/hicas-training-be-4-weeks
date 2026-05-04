using CommentAPI;

namespace CommentAPI.Services;

// Lớp cơ sở cho service: giữ tham chiếu cache phân tán + helper quyết định có lọc CreatedAt hay không.
public abstract class ServiceBase
{
    // Cache JSON entity/list — dùng GetJsonAsync/SetJsonAsync trong service con (Post, User, Comment…).
    protected readonly IEntityResponseCache Cache;

    // BƯỚC 1: Nhận IEntityResponseCache từ DI và gán vào trường protected.
    protected ServiceBase(IEntityResponseCache cache)
    {
        Cache = cache; // Một instance scoped mỗi HTTP request (thông thường).
    }

    // Mục đích: true khi client gửi ít nhất một biên ngày — dùng để tắt cache danh sách “mặc định” (tránh khóa bùng nổ / stale).
    // TRƯỜNG HỢP A: Chỉ có from → HasValue true.
    // TRƯỜNG HỢP B: Chỉ có to → HasValue true.
    // TRƯỜNG HỢP C: Cả hai null → false.
    protected static bool HasCreatedAtFilter(DateTime? createdAtFrom, DateTime? createdAtTo) =>
        createdAtFrom.HasValue || createdAtTo.HasValue; // OR logic: một trong hai đủ để coi là có lọc ngày.
}
