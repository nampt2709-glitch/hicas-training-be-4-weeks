using ApartmentAPI.Data; // AppDbContext — DbSet Invoice.
using ApartmentAPI.Entities; // Invoice POCO.
using Microsoft.EntityFrameworkCore; // EF Core truy vấn và lưu thay đổi.

namespace ApartmentAPI.Repositories;

// Hợp đồng repository Invoice: hóa đơn — lọc căn hộ, trạng thái, mã hóa đơn + phân trang sort.
public interface IInvoiceRepository
{ // Mở khối IInvoiceRepository.
    Task<List<Invoice>> GetAllAsync(CancellationToken ct = default);

    Task<(List<Invoice> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? apartmentId, // Lọc theo căn hộ.
        InvoiceStatus? status, // Draft/Paid...
        string? invoiceCodeContains, // Tìm mã hóa đơn chứa chuỗi.
        InvoiceListSort sort,
        CancellationToken ct = default);

    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Invoice?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);

    Task<List<Invoice>> GetByApartmentIdAsync(Guid apartmentId, CancellationToken ct = default); // Lịch sử hóa đơn một căn.

    Task AddAsync(Invoice entity, CancellationToken ct = default);
    void Update(Invoice entity);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
} // Kết thúc IInvoiceRepository.

// Truy vấn bảng Invoice — CRUD + phân trang + sort.
public sealed class InvoiceRepository : RepositoryBase<Invoice>, IInvoiceRepository
{ // Mở khối InvoiceRepository.
    public InvoiceRepository(AppDbContext context)
        : base(context)
    { // Mở khối constructor.
        // BƯỚC 1 — Khởi tạo base — không state cục bộ.
    } // Kết thúc constructor.

    public override Task<List<Invoice>> GetAllAsync(CancellationToken ct = default)
    { // Mở khối GetAllAsync.
        return base.GetAllAsync(ct);
    } // Kết thúc GetAllAsync.

    public async Task<(List<Invoice> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? apartmentId,
        InvoiceStatus? status,
        string? invoiceCodeContains,
        InvoiceListSort sort,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        // BƯỚC 1 — Normalize phân trang.
        var (p, s) = PaginationQuery.Normalize(page, pageSize);

        // BƯỚC 2 — IQueryable chỉ đọc Invoice.
        var q = Set.AsNoTracking().AsQueryable();

        // BƯỚC 3 — Khoảng CreatedAt.
        q = q.WhereCreatedAtRange(createdAtFrom, createdAtTo);

        // BƯỚC 4 — Lọc căn hộ nếu có.
        if (apartmentId is { } aid)
            q = q.Where(i => i.ApartmentId == aid);

        // BƯỚC 5 — Lọc trạng thái nếu có.
        if (status is { } st)
            q = q.Where(i => i.Status == st);

        // BƯỚC 6 — Contains mã hóa đơn nếu có từ khóa.
        var code = invoiceCodeContains?.Trim();
        if (!string.IsNullOrEmpty(code))
            q = q.Where(i => i.InvoiceCode.Contains(code));

        // BƯỚC 7 — COUNT.
        var total = await q.LongCountAsync(ct);

        // BƯỚC 8 — Sort + trang.
        q = ApplyInvoiceSort(q, sort);
        var items = await q.Skip((p - 1) * s).Take(s).ToListAsync(ct);

        return (items, total, p, s);
    } // Kết thúc GetPagedAsync.

    private static IQueryable<Invoice> ApplyInvoiceSort(IQueryable<Invoice> q, InvoiceListSort spec)
    { // Mở khối ApplyInvoiceSort.
        var desc = spec.Descending;
        return spec.Column switch
        {
            InvoiceSortColumn.Id => desc ? q.OrderByDescending(i => i.Id) : q.OrderBy(i => i.Id),
            InvoiceSortColumn.InvoiceCode => desc ? q.OrderByDescending(i => i.InvoiceCode) : q.OrderBy(i => i.InvoiceCode),
            InvoiceSortColumn.Year => desc ? q.OrderByDescending(i => i.Year).ThenByDescending(i => i.Month) : q.OrderBy(i => i.Year).ThenBy(i => i.Month),
            InvoiceSortColumn.Month => desc ? q.OrderByDescending(i => i.Month) : q.OrderBy(i => i.Month),
            InvoiceSortColumn.TotalAmount => desc ? q.OrderByDescending(i => i.TotalAmount) : q.OrderBy(i => i.TotalAmount),
            InvoiceSortColumn.Status => desc ? q.OrderByDescending(i => i.Status) : q.OrderBy(i => i.Status),
            InvoiceSortColumn.ApartmentId => desc ? q.OrderByDescending(i => i.ApartmentId) : q.OrderBy(i => i.ApartmentId),
            InvoiceSortColumn.CreatedAt => desc ? q.OrderByDescending(i => i.CreatedAt) : q.OrderBy(i => i.CreatedAt),
            _ => desc ? q.OrderByDescending(i => i.CreatedAt) : q.OrderBy(i => i.CreatedAt),
        };
    } // Kết thúc ApplyInvoiceSort.

    public override Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        return base.GetByIdAsync(id, ct);
    } // Kết thúc GetByIdAsync.

    public override Task<Invoice?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdTrackedAsync.
        return base.GetByIdTrackedAsync(id, ct);
    } // Kết thúc GetByIdTrackedAsync.

    public async Task<List<Invoice>> GetByApartmentIdAsync(Guid apartmentId, CancellationToken ct = default)
    { // Mở khối GetByApartmentIdAsync.
        // BƯỚC 1 — Hóa đơn mới theo kỳ (Year desc, Month desc) cho UX “gần nhất trước”.
        return await Set.AsNoTracking()
            .Where(i => i.ApartmentId == apartmentId)
            .OrderByDescending(i => i.Year).ThenByDescending(i => i.Month)
            .ToListAsync(ct);
    } // Kết thúc GetByApartmentIdAsync.

    public override Task AddAsync(Invoice entity, CancellationToken ct = default)
    { // Mở khối AddAsync.
        return base.AddAsync(entity, ct);
    } // Kết thúc AddAsync.

    public override void Update(Invoice entity)
    { // Mở khối Update.
        base.Update(entity);
    } // Kết thúc Update.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        var entity = await GetByIdTrackedAsync(id, ct);

        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Invoice not found.");

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
} // Kết thúc InvoiceRepository.
