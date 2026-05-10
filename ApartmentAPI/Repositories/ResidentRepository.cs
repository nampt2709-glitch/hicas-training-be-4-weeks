using ApartmentAPI.Data; // AppDbContext — DbSet Resident và truy vấn EF.
using ApartmentAPI.Entities; // Resident POCO map bảng cư dân.
using Microsoft.EntityFrameworkCore; // IQueryable, SaveChangesAsync, AsNoTracking.

namespace ApartmentAPI.Repositories;

// Hợp đồng repository Resident: cư dân — lọc căn hộ, họ tên, CMND/CCCD + phân trang sort.
public interface IResidentRepository
{ // Mở khối IResidentRepository.
    Task<List<Resident>> GetAllAsync(CancellationToken ct = default);

    Task<(List<Resident> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? apartmentId, // Cư dân thuộc căn.
        string? fullNameContains, // Tìm họ tên chứa.
        string? identityContains, // Tìm số định danh chứa.
        ResidentListSort sort,
        CancellationToken ct = default);

    Task<Resident?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Resident?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);

    Task<List<Resident>> GetByApartmentIdAsync(Guid apartmentId, CancellationToken ct = default); // Danh sách người trong căn.

    Task AddAsync(Resident entity, CancellationToken ct = default);
    void Update(Resident entity);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
} // Kết thúc IResidentRepository.

// Truy vấn bảng Resident — CRUD + phân trang + sort.
public sealed class ResidentRepository : RepositoryBase<Resident>, IResidentRepository
{ // Mở khối ResidentRepository.
    public ResidentRepository(AppDbContext context)
        : base(context)
    { // Mở khối constructor.
        // BƯỚC 1 — Base init — không state cục bộ.
    } // Kết thúc constructor.

    public override Task<List<Resident>> GetAllAsync(CancellationToken ct = default)
    { // Mở khối GetAllAsync.
        return base.GetAllAsync(ct);
    } // Kết thúc GetAllAsync.

    public async Task<(List<Resident> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? apartmentId,
        string? fullNameContains,
        string? identityContains,
        ResidentListSort sort,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        // BƯỚC 1 — Normalize phân trang.
        var (p, s) = PaginationQuery.Normalize(page, pageSize);

        // BƯỚC 2 — Query read-only Resident.
        var q = Set.AsNoTracking().AsQueryable();

        // BƯỚC 3 — Khoảng CreatedAt.
        q = q.WhereCreatedAtRange(createdAtFrom, createdAtTo);

        // BƯỚC 4 — Lọc căn hộ nếu có.
        if (apartmentId is { } aid)
            q = q.Where(r => r.ApartmentId == aid);

        // BƯỚC 5 — Contains họ tên nếu có.
        var fn = fullNameContains?.Trim();
        if (!string.IsNullOrEmpty(fn))
            q = q.Where(r => r.FullName.Contains(fn));

        // BƯỚC 6 — Contains số định danh nếu có.
        var idn = identityContains?.Trim();
        if (!string.IsNullOrEmpty(idn))
            q = q.Where(r => r.IdentityNumber.Contains(idn));

        // BƯỚC 7 — COUNT.
        var total = await q.LongCountAsync(ct);

        // BƯỚC 8 — Sort + trang.
        q = ApplyResidentSort(q, sort);
        var items = await q.Skip((p - 1) * s).Take(s).ToListAsync(ct);

        return (items, total, p, s);
    } // Kết thúc GetPagedAsync.

    private static IQueryable<Resident> ApplyResidentSort(IQueryable<Resident> q, ResidentListSort spec)
    { // Mở khối ApplyResidentSort.
        var desc = spec.Descending;
        return spec.Column switch
        {
            ResidentSortColumn.Id => desc ? q.OrderByDescending(r => r.Id) : q.OrderBy(r => r.Id),
            ResidentSortColumn.FullName => desc ? q.OrderByDescending(r => r.FullName) : q.OrderBy(r => r.FullName),
            ResidentSortColumn.IdentityNumber => desc ? q.OrderByDescending(r => r.IdentityNumber) : q.OrderBy(r => r.IdentityNumber),
            ResidentSortColumn.ApartmentId => desc ? q.OrderByDescending(r => r.ApartmentId) : q.OrderBy(r => r.ApartmentId),
            ResidentSortColumn.CreatedAt => desc ? q.OrderByDescending(r => r.CreatedAt) : q.OrderBy(r => r.CreatedAt),
            _ => desc ? q.OrderByDescending(r => r.CreatedAt) : q.OrderBy(r => r.CreatedAt),
        };
    } // Kết thúc ApplyResidentSort.

    public override Task<Resident?> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        return base.GetByIdAsync(id, ct);
    } // Kết thúc GetByIdAsync.

    public override Task<Resident?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdTrackedAsync.
        return base.GetByIdTrackedAsync(id, ct);
    } // Kết thúc GetByIdTrackedAsync.

    public async Task<List<Resident>> GetByApartmentIdAsync(Guid apartmentId, CancellationToken ct = default)
    { // Mở khối GetByApartmentIdAsync.
        // BƯỚC 1 — Danh sách cư dân một căn — sắp theo FullName alphabet.
        return await Set.AsNoTracking().Where(r => r.ApartmentId == apartmentId).OrderBy(r => r.FullName).ToListAsync(ct);
    } // Kết thúc GetByApartmentIdAsync.

    public override Task AddAsync(Resident entity, CancellationToken ct = default)
    { // Mở khối AddAsync.
        return base.AddAsync(entity, ct);
    } // Kết thúc AddAsync.

    public override void Update(Resident entity)
    { // Mở khối Update.
        base.Update(entity);
    } // Kết thúc Update.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        var entity = await GetByIdTrackedAsync(id, ct);

        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Resident not found.");

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
} // Kết thúc ResidentRepository.
