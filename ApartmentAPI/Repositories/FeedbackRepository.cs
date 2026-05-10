using ApartmentAPI.Data; // AppDbContext — DbSet Feedback.
using ApartmentAPI.Entities; // Feedback POCO.
using Microsoft.EntityFrameworkCore; // EF Core truy vấn và lưu thay đổi.

namespace ApartmentAPI.Repositories;

// Hợp đồng repository Feedback: CRUD + cây (gốc/con) + lọc user/nội dung + phân trang sort.
public interface IFeedbackRepository
{ // Mở khối IFeedbackRepository.
    Task<List<Feedback>> GetAllAsync(CancellationToken ct = default); // Toàn bộ feedback.

    Task<(List<Feedback> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId, // Tác giả feedback.
        bool rootsOnly, // true: chỉ ParentId null.
        string? contentContains, // Full-text contains nội dung (substring).
        FeedbackListSort sort,
        CancellationToken ct = default);

    Task<Feedback?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Feedback?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);

    Task<List<Feedback>> GetByUserIdAsync(Guid userId, CancellationToken ct = default); // Mọi feedback của user.

    Task<List<Feedback>> GetRootsAsync(CancellationToken ct = default); // Chỉ gốc cây (ParentId null).

    Task AddAsync(Feedback entity, CancellationToken ct = default);
    void Update(Feedback entity);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);

    // Toàn bộ cặp (Id, ParentId) bản ghi còn sống — BFS subtree khi admin đổi ParentId (chống chu trình).
    Task<List<(Guid Id, Guid? ParentId)>> GetAllIdParentPairsAsync(CancellationToken ct = default);
} // Kết thúc IFeedbackRepository.

// Truy vấn bảng Feedback — phân trang, sort, lọc cây phản hồi.
public sealed class FeedbackRepository : RepositoryBase<Feedback>, IFeedbackRepository
{ // Mở khối FeedbackRepository.
    public FeedbackRepository(AppDbContext context)
        : base(context)
    { // Mở khối constructor.
        // BƯỚC 1 — base context — không state thêm.
    } // Kết thúc constructor.

    public override Task<List<Feedback>> GetAllAsync(CancellationToken ct = default)
    { // Mở khối GetAllAsync.
        return base.GetAllAsync(ct);
    } // Kết thúc GetAllAsync.

    public async Task<(List<Feedback> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        bool rootsOnly,
        string? contentContains,
        FeedbackListSort sort,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        // BƯỚC 1 — Chuẩn hóa phân trang.
        var (p, s) = PaginationQuery.Normalize(page, pageSize);

        // BƯỚC 2 — IQueryable chỉ đọc Feedback.
        var q = Set.AsNoTracking().AsQueryable();

        // BƯỚC 3 — Lọc CreatedAt.
        q = q.WhereCreatedAtRange(createdAtFrom, createdAtTo);

        // BƯỚC 4 — Lọc user nếu có.
        if (userId is { } uid)
            q = q.Where(f => f.UserId == uid);

        // BƯỚC 5 — Chỉ nút gốc nếu rootsOnly.
        if (rootsOnly)
            q = q.Where(f => f.ParentId == null);

        // BƯỚC 6 — Contains nội dung nếu có từ khóa.
        var txt = contentContains?.Trim();
        if (!string.IsNullOrEmpty(txt))
            q = q.Where(f => f.Content.Contains(txt));

        // BƯỚC 7 — COUNT.
        var total = await q.LongCountAsync(ct);

        // BƯỚC 8 — Sort + trang.
        q = ApplyFeedbackSort(q, sort);
        var items = await q.Skip((p - 1) * s).Take(s).ToListAsync(ct);

        return (items, total, p, s);
    } // Kết thúc GetPagedAsync.

    private static IQueryable<Feedback> ApplyFeedbackSort(IQueryable<Feedback> q, FeedbackListSort spec)
    { // Mở khối ApplyFeedbackSort.
        // BƯỚC 1 — Hướng sort.
        var desc = spec.Descending;

        // BƯỚC 2 — Whitelist: IsPinned có ThenBy CreatedAt để ổn định.
        return spec.Column switch
        {
            FeedbackSortColumn.Id => desc ? q.OrderByDescending(f => f.Id) : q.OrderBy(f => f.Id),
            FeedbackSortColumn.UserId => desc ? q.OrderByDescending(f => f.UserId) : q.OrderBy(f => f.UserId),
            FeedbackSortColumn.IsPinned => desc
                ? q.OrderByDescending(f => f.IsPinned).ThenByDescending(f => f.CreatedAt)
                : q.OrderBy(f => f.IsPinned).ThenBy(f => f.CreatedAt),
            FeedbackSortColumn.IsResolved => desc ? q.OrderByDescending(f => f.IsResolved) : q.OrderBy(f => f.IsResolved),
            FeedbackSortColumn.ParentId => desc ? q.OrderByDescending(f => f.ParentId) : q.OrderBy(f => f.ParentId),
            FeedbackSortColumn.CreatedAt => desc ? q.OrderByDescending(f => f.CreatedAt) : q.OrderBy(f => f.CreatedAt),
            _ => desc ? q.OrderByDescending(f => f.CreatedAt) : q.OrderBy(f => f.CreatedAt),
        };
    } // Kết thúc ApplyFeedbackSort.

    public override Task<Feedback?> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        return base.GetByIdAsync(id, ct);
    } // Kết thúc GetByIdAsync.

    public override Task<Feedback?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdTrackedAsync.
        return base.GetByIdTrackedAsync(id, ct);
    } // Kết thúc GetByIdTrackedAsync.

    public async Task<List<Feedback>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    { // Mở khối GetByUserIdAsync.
        // BƯỚC 1 — Mới nhất trước (CreatedAt desc).
        return await Set.AsNoTracking().Where(f => f.UserId == userId).OrderByDescending(f => f.CreatedAt).ToListAsync(ct);
    } // Kết thúc GetByUserIdAsync.

    public async Task<List<Feedback>> GetRootsAsync(CancellationToken ct = default)
    { // Mở khối GetRootsAsync.
        // BƯỚC 1 — Gốc cây: Pinned trước, rồi CreatedAt tăng.
        return await Set.AsNoTracking().Where(f => f.ParentId == null).OrderByDescending(f => f.IsPinned).ThenBy(f => f.CreatedAt).ToListAsync(ct);
    } // Kết thúc GetRootsAsync.

    public override Task AddAsync(Feedback entity, CancellationToken ct = default)
    { // Mở khối AddAsync.
        return base.AddAsync(entity, ct);
    } // Kết thúc AddAsync.

    public override void Update(Feedback entity)
    { // Mở khối Update.
        base.Update(entity);
    } // Kết thúc Update.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        var entity = await GetByIdTrackedAsync(id, ct);

        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Feedback not found.");

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

    // BƯỚC — Project nhẹ mọi nút feedback (đã áp global filter soft delete) để BFS subtree trong service.
    public async Task<List<(Guid Id, Guid? ParentId)>> GetAllIdParentPairsAsync(CancellationToken ct = default)
    { // Mở khối GetAllIdParentPairsAsync.
        return await Set.AsNoTracking()
            .Select(f => new ValueTuple<Guid, Guid?>(f.Id, f.ParentId))
            .ToListAsync(ct);
    } // Kết thúc GetAllIdParentPairsAsync.
} // Kết thúc FeedbackRepository.
