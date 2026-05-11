using ApartmentAPI.Data; // AppDbContext.
using ApartmentAPI.Entities; // Attachment, AttachmentScope.
using Microsoft.EntityFrameworkCore; // EF Core.

namespace ApartmentAPI.Repositories;

// Hợp đồng repository Attachment: CRUD + lọc FK (user/feedback) + scope + phân trang sort.
public interface IAttachmentRepository
{ // Mở khối IAttachmentRepository.
    Task<List<Attachment>> GetAllAsync(CancellationToken ct = default); // Toàn bộ (CreatedAt order base).

    Task<(List<Attachment> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId, // Lọc người tải.
        Guid? feedbackId, // Lọc feedback đính kèm.
        Guid? postId, // Lọc bài đăng đính kèm.
        AttachmentScope? scope, // Lọc phạ vi đính kèm.
        string? originalFileNameContains, // Tìm theo tên file gốc.
        AttachmentListSort sort,
        CancellationToken ct = default);

    Task<Attachment?> GetByIdAsync(Guid id, CancellationToken ct = default); // Đọc không track.
    Task<Attachment?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default); // Track để sửa/xóa mềm.

    Task<List<Attachment>> GetByUserIdAsync(Guid userId, CancellationToken ct = default); // Mọi file của user.

    // Mọi đính kèm scope Avatar còn hiệu lực của user — phục vụ thay avatar mới (xóa mềm + xóa file đĩa).
    Task<List<(Guid Id, string FilePath)>> GetActiveAvatarIdAndPathsForUserAsync(Guid userId, CancellationToken ct = default);
    Task<List<Attachment>> GetByFeedbackIdAsync(Guid feedbackId, CancellationToken ct = default); // Theo feedback.
    Task<List<Attachment>> GetByScopeAsync(AttachmentScope scope, CancellationToken ct = default); // Theo scope.

    Task AddAsync(Attachment entity, CancellationToken ct = default);
    void Update(Attachment entity);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
} // Kết thúc IAttachmentRepository.

// Truy vấn bảng Attachment — lọc đa điều kiện + sort whitelist + phân trang.
public sealed class AttachmentRepository : RepositoryBase<Attachment>, IAttachmentRepository
{ // Mở khối AttachmentRepository.
    public AttachmentRepository(AppDbContext context)
        : base(context)
    { // Mở khối constructor.
        // BƯỚC 1 — base đã gán Context — không trường bổ sung.
    } // Kết thúc constructor.

    public override Task<List<Attachment>> GetAllAsync(CancellationToken ct = default) // Ủy quyền.
    { // Mở khối GetAllAsync.
        // BƯỚC 1 — RepositoryBase GetAll.
        return base.GetAllAsync(ct);
    } // Kết thúc GetAllAsync.

    public async Task<(List<Attachment> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        Guid? feedbackId,
        Guid? postId,
        AttachmentScope? scope,
        string? originalFileNameContains,
        AttachmentListSort sort,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        // BƯỚC 1 — Chuẩn hóa phân trang.
        var (p, s) = PaginationQuery.Normalize(page, pageSize);

        // BƯỚC 2 — Truy vấn chỉ đọc Attachment.
        var q = Set.AsNoTracking().AsQueryable();

        // BƯỚC 3 — Lọc CreatedAt.
        q = q.WhereCreatedAtRange(createdAtFrom, createdAtTo);

        // BƯỚC 4 — Lọc UserId nếu có.
        if (userId is { } uid)
            q = q.Where(a => a.UserId == uid);

        // BƯỚC 5 — Lọc FeedbackId nếu có.
        if (feedbackId is { } fid)
            q = q.Where(a => a.FeedbackId == fid);

        // BƯỚC 5b — Lọc PostId nếu có.
        if (postId is { } pid)
            q = q.Where(a => a.PostId == pid);

        // BƯỚC 6 — Lọc Scope nếu có.
        if (scope is { } sc)
            q = q.Where(a => a.Scope == sc);

        // BƯỚC 7 — Contains tên file gốc nếu chuỗi khác rỗng.
        var name = originalFileNameContains?.Trim();
        if (!string.IsNullOrEmpty(name))
            q = q.Where(a => a.OriginalFileName.Contains(name));

        // BƯỚC 8 — COUNT metadata.
        var total = await q.LongCountAsync(ct);

        // BƯỚC 9 — Sort + một trang.
        q = ApplyAttachmentSort(q, sort);
        var items = await q.Skip((p - 1) * s).Take(s).ToListAsync(ct);

        return (items, total, p, s);
    } // Kết thúc GetPagedAsync.

    private static IQueryable<Attachment> ApplyAttachmentSort(IQueryable<Attachment> q, AttachmentListSort spec)
    { // Mở khối ApplyAttachmentSort.
        // BƯỚC 1 — Cờ chiều sort.
        var desc = spec.Descending;

        // BƯỚC 2 — Whitelist cột → OrderBy.
        return spec.Column switch
        {
            AttachmentSortColumn.Id => desc ? q.OrderByDescending(a => a.Id) : q.OrderBy(a => a.Id),
            AttachmentSortColumn.Scope => desc ? q.OrderByDescending(a => a.Scope) : q.OrderBy(a => a.Scope),
            AttachmentSortColumn.UserId => desc ? q.OrderByDescending(a => a.UserId) : q.OrderBy(a => a.UserId),
            AttachmentSortColumn.FeedbackId => desc ? q.OrderByDescending(a => a.FeedbackId) : q.OrderBy(a => a.FeedbackId),
            AttachmentSortColumn.PostId => desc ? q.OrderByDescending(a => a.PostId) : q.OrderBy(a => a.PostId),
            AttachmentSortColumn.OriginalFileName => desc ? q.OrderByDescending(a => a.OriginalFileName) : q.OrderBy(a => a.OriginalFileName),
            AttachmentSortColumn.CreatedAt => desc ? q.OrderByDescending(a => a.CreatedAt) : q.OrderBy(a => a.CreatedAt),
            _ => desc ? q.OrderByDescending(a => a.CreatedAt) : q.OrderBy(a => a.CreatedAt),
        };
    } // Kết thúc ApplyAttachmentSort.

    public override Task<Attachment?> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        return base.GetByIdAsync(id, ct);
    } // Kết thúc GetByIdAsync.

    public override Task<Attachment?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdTrackedAsync.
        return base.GetByIdTrackedAsync(id, ct);
    } // Kết thúc GetByIdTrackedAsync.

    public async Task<List<Attachment>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    { // Mở khối GetByUserIdAsync.
        // BƯỚC 1 — AsNoTracking Where UserId, materialize.
        return await Set.AsNoTracking().Where(a => a.UserId == userId).ToListAsync(ct);
    } // Kết thúc GetByUserIdAsync.

    public async Task<List<(Guid Id, string FilePath)>> GetActiveAvatarIdAndPathsForUserAsync(Guid userId, CancellationToken ct = default)
    { // Mở khối GetActiveAvatarIdAndPathsForUserAsync — global filter đã loại IsDeleted.
        return await Set.AsNoTracking()
            .Where(a => a.UserId == userId && a.Scope == AttachmentScope.Avatar)
            .Select(a => new ValueTuple<Guid, string>(a.Id, a.FilePath))
            .ToListAsync(ct);
    } // Kết thúc GetActiveAvatarIdAndPathsForUserAsync.

    public async Task<List<Attachment>> GetByFeedbackIdAsync(Guid feedbackId, CancellationToken ct = default)
    { // Mở khối GetByFeedbackIdAsync.
        // BƯỚC 1 — Mọi attachment thuộc một feedback.
        return await Set.AsNoTracking().Where(a => a.FeedbackId == feedbackId).ToListAsync(ct);
    } // Kết thúc GetByFeedbackIdAsync.

    public async Task<List<Attachment>> GetByScopeAsync(AttachmentScope scope, CancellationToken ct = default)
    { // Mở khối GetByScopeAsync.
        // BƯỚC 1 — Lọc enum scope (Avatar / Feedback / Post).
        return await Set.AsNoTracking().Where(a => a.Scope == scope).ToListAsync(ct);
    } // Kết thúc GetByScopeAsync.

    public override Task AddAsync(Attachment entity, CancellationToken ct = default)
    { // Mở khối AddAsync.
        return base.AddAsync(entity, ct);
    } // Kết thúc AddAsync.

    public override void Update(Attachment entity)
    { // Mở khối Update.
        base.Update(entity);
    } // Kết thúc Update.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        // BƯỚC 1 — Nạp tracked.
        var entity = await GetByIdTrackedAsync(id, ct);

        // TRƯỜNG HỢP: không tồn tại.
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Attachment not found.");

        // BƯỚC 2 — Soft delete base.
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
} // Kết thúc AttachmentRepository.
