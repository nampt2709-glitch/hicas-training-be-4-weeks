using CommentAPI; // IEntityResponseCache — cache JSON entity/list trong distributed backend.

namespace CommentAPI.Services;

// Lớp abstract: giữ Cache + helper HasCreatedAtFilter — dùng chung PostService, UserService, CommentService (kế thừa ServiceBase).
public abstract class ServiceBase
{
    // Cache JSON (scoped) — con gọi GetJsonAsync / SetJsonAsync / Remove*.
    protected readonly IEntityResponseCache Cache;

    // BƯỚC 1 — Tiêm cache từ DI; base(cache) được gọi từ constructor lớp con.
    protected ServiceBase(IEntityResponseCache cache)
    { // Mở constructor ServiceBase.
        Cache = cache; // Một IEntityResponseCache mỗi HTTP request (thông thường).
    } // Kết thúc constructor.

    // Mục đích: true khi client gửi ít nhất một biên ngày CreatedAt — tắt cache list “mặc định” (không gian khóa + tránh stale).
    // TRƯỜNG HỢP A: chỉ from → HasValue true.
    // TRƯỜNG HỢP B: chỉ to → HasValue true.
    // TRƯỜNG HỢP C: cả hai null → false.
    protected static bool HasCreatedAtFilter(DateTime? createdAtFrom, DateTime? createdAtTo) =>
        createdAtFrom.HasValue || createdAtTo.HasValue;
} // Kết thúc lớp ServiceBase.
