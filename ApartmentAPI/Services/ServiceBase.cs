namespace ApartmentAPI.Services;

// Lớp abstract: cache JSON phản hồi + epoch làm mới khóa danh sách + helper điều kiện lọc — các service nghiệp vụ kế thừa để trùng ctor và quy tắc “có filter thì không cache trang mặc định”.
public abstract class ServiceBase
{
    // Cache theo khóa entity/trang (scoped) — lớp con gọi GetJsonAsync / SetJsonAsync / RemoveAsync.
    protected readonly IEntityResponseCache Cache;

    // Bump epoch để vô hiệu hóa mọi trang danh sách đã embed epoch cũ — dùng sau CUD.
    protected readonly ICacheListEpochStore ListEpoch;

    // BƯỚC 1 — Tiêm cache + epoch từ DI; constructor lớp con gọi base(cache, listEpoch) sau khi nhận cùng tham số.
    protected ServiceBase(IEntityResponseCache cache, ICacheListEpochStore listEpoch)
    {
        Cache = cache;
        ListEpoch = listEpoch;
    }

    // True khi client gửi ít nhất một biên CreatedAt — tắt cache list “sạch” (tránh stale / không gian khóa lớn).
    protected static bool HasCreatedAtFilter(DateTime? createdAtFrom, DateTime? createdAtTo) =>
        createdAtFrom.HasValue || createdAtTo.HasValue;

    // True khi chuỗi lọc (contains/LIKE) có nội dung sau khi loại khoảng trắng đầu cuối — dùng trong Has…ListFilter.
    protected static bool HasTextFilter(string? value) =>
        !string.IsNullOrWhiteSpace(value);
}
