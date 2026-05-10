using ApartmentAPI.Data; // AppDbContext — DbSet RefreshToken.
using ApartmentAPI.Entities; // RefreshToken POCO.
using Microsoft.EntityFrameworkCore; // EF Core truy vấn và lưu thay đổi.

namespace ApartmentAPI.Repositories;

// Hợp đồng repository RefreshToken: token làm mới — lọc user, trạng thái revoke + phân trang sort.
public interface IRefreshTokenRepository
{ // Mở khối IRefreshTokenRepository.
    Task<List<RefreshToken>> GetAllAsync(CancellationToken ct = default);

    Task<(List<RefreshToken> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId, // Chủ sở hữu token.
        bool? isRevoked, // true/false hoặc null = mọi trạng thái.
        RefreshTokenListSort sort,
        CancellationToken ct = default);

    Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RefreshToken?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);

    Task<List<RefreshToken>> GetByUserIdAsync(Guid userId, CancellationToken ct = default); // Mọi phiên refresh của user.

    Task AddAsync(RefreshToken entity, CancellationToken ct = default);
    void Update(RefreshToken entity);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
} // Kết thúc IRefreshTokenRepository.

// Truy vấn bảng RefreshToken — CRUD + phân trang.
public sealed class RefreshTokenRepository : RepositoryBase<RefreshToken>, IRefreshTokenRepository
{ // Mở khối RefreshTokenRepository.
    public RefreshTokenRepository(AppDbContext context)
        : base(context)
    { // Mở khối constructor.
        // BƯỚC 1 — Base context — không field bổ sung.
    } // Kết thúc constructor.

    public override Task<List<RefreshToken>> GetAllAsync(CancellationToken ct = default)
    { // Mở khối GetAllAsync.
        return base.GetAllAsync(ct);
    } // Kết thúc GetAllAsync.

    public async Task<(List<RefreshToken> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        bool? isRevoked,
        RefreshTokenListSort sort,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        // BƯỚC 1 — Chuẩn hóa page/size.
        var (p, s) = PaginationQuery.Normalize(page, pageSize);

        // BƯỚC 2 — Query read-only.
        var q = Set.AsNoTracking().AsQueryable();

        // BƯỚC 3 — Lọc CreatedAt.
        q = q.WhereCreatedAtRange(createdAtFrom, createdAtTo);

        // BƯỚC 4 — User nếu có.
        if (userId is { } uid)
            q = q.Where(t => t.UserId == uid);

        // BƯỚC 5 — Revoked cụ thể nếu có (null = không lọc).
        if (isRevoked is { } rv)
            q = q.Where(t => t.IsRevoked == rv);

        // BƯỚC 6 — COUNT.
        var total = await q.LongCountAsync(ct);

        // BƯỚC 7 — Sort + trang.
        q = ApplyRefreshTokenSort(q, sort);
        var items = await q.Skip((p - 1) * s).Take(s).ToListAsync(ct);

        return (items, total, p, s);
    } // Kết thúc GetPagedAsync.

    private static IQueryable<RefreshToken> ApplyRefreshTokenSort(IQueryable<RefreshToken> q, RefreshTokenListSort spec)
    { // Mở khối ApplyRefreshTokenSort.
        var desc = spec.Descending;
        return spec.Column switch
        {
            RefreshTokenSortColumn.Id => desc ? q.OrderByDescending(t => t.Id) : q.OrderBy(t => t.Id),
            RefreshTokenSortColumn.UserId => desc ? q.OrderByDescending(t => t.UserId) : q.OrderBy(t => t.UserId),
            RefreshTokenSortColumn.ExpiresAt => desc ? q.OrderByDescending(t => t.ExpiresAt) : q.OrderBy(t => t.ExpiresAt),
            RefreshTokenSortColumn.IsRevoked => desc ? q.OrderByDescending(t => t.IsRevoked) : q.OrderBy(t => t.IsRevoked),
            RefreshTokenSortColumn.CreatedAt => desc ? q.OrderByDescending(t => t.CreatedAt) : q.OrderBy(t => t.CreatedAt),
            _ => desc ? q.OrderByDescending(t => t.CreatedAt) : q.OrderBy(t => t.CreatedAt),
        };
    } // Kết thúc ApplyRefreshTokenSort.

    public override Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        return base.GetByIdAsync(id, ct);
    } // Kết thúc GetByIdAsync.

    public override Task<RefreshToken?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdTrackedAsync.
        return base.GetByIdTrackedAsync(id, ct);
    } // Kết thúc GetByIdTrackedAsync.

    public async Task<List<RefreshToken>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    { // Mở khối GetByUserIdAsync.
        // BƯỚC 1 — Mọi refresh token của một user (không sort cứng — service có thể sort thêm).
        return await Set.AsNoTracking().Where(t => t.UserId == userId).ToListAsync(ct);
    } // Kết thúc GetByUserIdAsync.

    public override Task AddAsync(RefreshToken entity, CancellationToken ct = default)
    { // Mở khối AddAsync.
        return base.AddAsync(entity, ct);
    } // Kết thúc AddAsync.

    public override void Update(RefreshToken entity)
    { // Mở khối Update.
        base.Update(entity);
    } // Kết thúc Update.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        var entity = await GetByIdTrackedAsync(id, ct);

        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Refresh token not found.");

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
} // Kết thúc RefreshTokenRepository.
