using ApartmentAPI.Data; // AppDbContext — DbSet InvoiceItem.
using ApartmentAPI.Entities; // InvoiceItem POCO.
using Microsoft.EntityFrameworkCore; // EF Core truy vấn và lưu thay đổi.

namespace ApartmentAPI.Repositories;

// Hợp đồng repository InvoiceItem: dòng chi tiết hóa đơn — lọc invoice/service + phân trang sort.
public interface IInvoiceItemRepository
{ // Mở khối IInvoiceItemRepository.
    Task<List<InvoiceItem>> GetAllAsync(CancellationToken ct = default);

    Task<(List<InvoiceItem> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? invoiceId, // Lọc theo hóa đơn cha.
        Guid? serviceId, // Lọc theo dịch vụ tiện ích.
        InvoiceItemListSort sort,
        CancellationToken ct = default);

    Task<InvoiceItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<InvoiceItem?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);

    Task<List<InvoiceItem>> GetByInvoiceIdAsync(Guid invoiceId, CancellationToken ct = default); // Mọi dòng của một invoice.

    Task AddAsync(InvoiceItem entity, CancellationToken ct = default);
    void Update(InvoiceItem entity);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
} // Kết thúc IInvoiceItemRepository.

// Truy vấn bảng InvoiceItem — CRUD + phân trang + sort whitelist.
public sealed class InvoiceItemRepository : RepositoryBase<InvoiceItem>, IInvoiceItemRepository
{ // Mở khối InvoiceItemRepository.
    public InvoiceItemRepository(AppDbContext context)
        : base(context)
    { // Mở khối constructor.
        // BƯỚC 1 — base gán Context — không trường thêm.
    } // Kết thúc constructor.

    public override Task<List<InvoiceItem>> GetAllAsync(CancellationToken ct = default)
    { // Mở khối GetAllAsync.
        return base.GetAllAsync(ct);
    } // Kết thúc GetAllAsync.

    public async Task<(List<InvoiceItem> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? invoiceId,
        Guid? serviceId,
        InvoiceItemListSort sort,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        // BƯỚC 1 — Chuẩn hóa trang.
        var (p, s) = PaginationQuery.Normalize(page, pageSize);

        // BƯỚC 2 — Query chỉ đọc.
        var q = Set.AsNoTracking().AsQueryable();

        // BƯỚC 3 — Lọc CreatedAt.
        q = q.WhereCreatedAtRange(createdAtFrom, createdAtTo);

        // BƯỚC 4 — Lọc InvoiceId nếu có.
        if (invoiceId is { } iid)
            q = q.Where(x => x.InvoiceId == iid);

        // BƯỚC 5 — Lọc ServiceId nếu có.
        if (serviceId is { } sid)
            q = q.Where(x => x.ServiceId == sid);

        // BƯỚC 6 — COUNT.
        var total = await q.LongCountAsync(ct);

        // BƯỚC 7 — Sort + một trang.
        q = ApplyInvoiceItemSort(q, sort);
        var items = await q.Skip((p - 1) * s).Take(s).ToListAsync(ct);

        return (items, total, p, s);
    } // Kết thúc GetPagedAsync.

    private static IQueryable<InvoiceItem> ApplyInvoiceItemSort(IQueryable<InvoiceItem> q, InvoiceItemListSort spec)
    { // Mở khối ApplyInvoiceItemSort.
        var desc = spec.Descending;
        return spec.Column switch
        {
            InvoiceItemSortColumn.Id => desc ? q.OrderByDescending(x => x.Id) : q.OrderBy(x => x.Id),
            InvoiceItemSortColumn.InvoiceId => desc ? q.OrderByDescending(x => x.InvoiceId) : q.OrderBy(x => x.InvoiceId),
            InvoiceItemSortColumn.ServiceId => desc ? q.OrderByDescending(x => x.ServiceId) : q.OrderBy(x => x.ServiceId),
            InvoiceItemSortColumn.SubTotal => desc ? q.OrderByDescending(x => x.SubTotal) : q.OrderBy(x => x.SubTotal),
            InvoiceItemSortColumn.CreatedAt => desc ? q.OrderByDescending(x => x.CreatedAt) : q.OrderBy(x => x.CreatedAt),
            _ => desc ? q.OrderByDescending(x => x.CreatedAt) : q.OrderBy(x => x.CreatedAt),
        };
    } // Kết thúc ApplyInvoiceItemSort.

    public override Task<InvoiceItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        return base.GetByIdAsync(id, ct);
    } // Kết thúc GetByIdAsync.

    public override Task<InvoiceItem?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdTrackedAsync.
        return base.GetByIdTrackedAsync(id, ct);
    } // Kết thúc GetByIdTrackedAsync.

    public async Task<List<InvoiceItem>> GetByInvoiceIdAsync(Guid invoiceId, CancellationToken ct = default)
    { // Mở khối GetByInvoiceIdAsync.
        // BƯỚC 1 — Mọi dòng chi tiết thuộc một hóa đơn.
        return await Set.AsNoTracking().Where(i => i.InvoiceId == invoiceId).ToListAsync(ct);
    } // Kết thúc GetByInvoiceIdAsync.

    public override Task AddAsync(InvoiceItem entity, CancellationToken ct = default)
    { // Mở khối AddAsync.
        return base.AddAsync(entity, ct);
    } // Kết thúc AddAsync.

    public override void Update(InvoiceItem entity)
    { // Mở khối Update.
        base.Update(entity);
    } // Kết thúc Update.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        var entity = await GetByIdTrackedAsync(id, ct);

        // TRƯỜNG HỢP: không có dòng chi tiết.
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Invoice item not found.");

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
} // Kết thúc InvoiceItemRepository.
