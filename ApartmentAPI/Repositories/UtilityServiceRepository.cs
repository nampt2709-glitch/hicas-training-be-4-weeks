using ApartmentAPI.Data; // AppDbContext — DbSet UtilityService.
using ApartmentAPI.Entities; // UtilityService POCO.
using Microsoft.EntityFrameworkCore; // EF Core truy vấn và lưu thay đổi.

namespace ApartmentAPI.Repositories;

// Hợp đồng repository UtilityService: dịch vụ tiện ích (giá, đơn vị) — lọc active + tên + phân trang sort.
public interface IUtilityServiceRepository
{ // Mở khối IUtilityServiceRepository.
    Task<List<UtilityService>> GetAllAsync(CancellationToken ct = default);

    Task<(List<UtilityService> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        bool? isActive, // true/false hoặc null = mọi bản ghi.
        string? nameContains, // Tên dịch vụ chứa chuỗi.
        UtilityListSort sort,
        CancellationToken ct = default);

    Task<UtilityService?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UtilityService?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);

    Task<List<UtilityService>> GetActiveAsync(CancellationToken ct = default); // Catalog đang bật (đặt hàng/hóa đơn).

    Task AddAsync(UtilityService entity, CancellationToken ct = default);
    void Update(UtilityService entity);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
} // Kết thúc IUtilityServiceRepository.

// Truy vấn bảng UtilityService — CRUD + phân trang + sort.
public sealed class UtilityServiceRepository : RepositoryBase<UtilityService>, IUtilityServiceRepository
{ // Mở khối UtilityServiceRepository.
    public UtilityServiceRepository(AppDbContext context)
        : base(context)
    { // Mở khối constructor.
        // BƯỚC 1 — Base context — không field thêm.
    } // Kết thúc constructor.

    public override Task<List<UtilityService>> GetAllAsync(CancellationToken ct = default)
    { // Mở khối GetAllAsync.
        return base.GetAllAsync(ct);
    } // Kết thúc GetAllAsync.

    public async Task<(List<UtilityService> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        bool? isActive,
        string? nameContains,
        UtilityListSort sort,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        // BƯỚC 1 — Normalize page/size.
        var (p, s) = PaginationQuery.Normalize(page, pageSize);

        // BƯỚC 2 — Query read-only.
        var q = Set.AsNoTracking().AsQueryable();

        // BƯỚC 3 — Khoảng CreatedAt.
        q = q.WhereCreatedAtRange(createdAtFrom, createdAtTo);

        // BƯỚC 4 — IsActive nếu có giá trị bool.
        if (isActive is { } act)
            q = q.Where(u => u.IsActive == act);

        // BƯỚC 5 — Contains Name nếu có keyword.
        var nm = nameContains?.Trim();
        if (!string.IsNullOrEmpty(nm))
            q = q.Where(u => u.Name.Contains(nm));

        // BƯỚC 6 — COUNT.
        var total = await q.LongCountAsync(ct);

        // BƯỚC 7 — Sort + trang.
        q = ApplyUtilitySort(q, sort);
        var items = await q.Skip((p - 1) * s).Take(s).ToListAsync(ct);

        return (items, total, p, s);
    } // Kết thúc GetPagedAsync.

    private static IQueryable<UtilityService> ApplyUtilitySort(IQueryable<UtilityService> q, UtilityListSort spec)
    { // Mở khối ApplyUtilitySort.
        var desc = spec.Descending;
        return spec.Column switch
        {
            UtilitySortColumn.Id => desc ? q.OrderByDescending(u => u.Id) : q.OrderBy(u => u.Id),
            UtilitySortColumn.Name => desc ? q.OrderByDescending(u => u.Name) : q.OrderBy(u => u.Name),
            UtilitySortColumn.Price => desc ? q.OrderByDescending(u => u.Price) : q.OrderBy(u => u.Price),
            UtilitySortColumn.Unit => desc ? q.OrderByDescending(u => u.Unit) : q.OrderBy(u => u.Unit),
            UtilitySortColumn.IsActive => desc ? q.OrderByDescending(u => u.IsActive) : q.OrderBy(u => u.IsActive),
            UtilitySortColumn.CreatedAt => desc ? q.OrderByDescending(u => u.CreatedAt) : q.OrderBy(u => u.CreatedAt),
            _ => desc ? q.OrderByDescending(u => u.CreatedAt) : q.OrderBy(u => u.CreatedAt),
        };
    } // Kết thúc ApplyUtilitySort.

    public override Task<UtilityService?> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        return base.GetByIdAsync(id, ct);
    } // Kết thúc GetByIdAsync.

    public override Task<UtilityService?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdTrackedAsync.
        return base.GetByIdTrackedAsync(id, ct);
    } // Kết thúc GetByIdTrackedAsync.

    public async Task<List<UtilityService>> GetActiveAsync(CancellationToken ct = default)
    { // Mở khối GetActiveAsync.
        // BƯỚC 1 — Chỉ dịch vụ đang hoạt động — sắp Name để dropdown ổn định.
        return await Set.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync(ct);
    } // Kết thúc GetActiveAsync.

    public override Task AddAsync(UtilityService entity, CancellationToken ct = default)
    { // Mở khối AddAsync.
        return base.AddAsync(entity, ct);
    } // Kết thúc AddAsync.

    public override void Update(UtilityService entity)
    { // Mở khối Update.
        base.Update(entity);
    } // Kết thúc Update.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        var entity = await GetByIdTrackedAsync(id, ct);

        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Utility service not found.");

        SoftDelete(entity, deletedBy);
    } // Kết thúc SoftDeleteAsync.

    public override Task SaveChangesAsync(CancellationToken ct = default)
    { // Mở khối SaveChangesAsync.
        return base.SaveChangesAsync(ct);
    } // Kết thúc SaveChangesAsync.

    public override Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    { // Mở khối ExistsAsync.
        return base.ExistsAsync(id, ct);
    } // Kết thúc ExistsAsync.
} // Kết thúc UtilityServiceRepository.
