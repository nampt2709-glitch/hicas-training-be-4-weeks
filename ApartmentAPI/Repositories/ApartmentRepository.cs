using ApartmentAPI.Data; // AppDbContext: DbSet Apartment.
using ApartmentAPI.Entities; // Entity Apartment + enum trạng thái.
using Microsoft.EntityFrameworkCore; // EF Core: AsNoTracking, LongCountAsync, Where, v.v.

namespace ApartmentAPI.Repositories;

// Hợp đồng repository Apartment: CRUD + lọc trạng thái + phân trang có sort whitelist.
public interface IApartmentRepository
{ // Mở khối IApartmentRepository.
    // Lấy toàn bộ apartment không phân trang (thứ tự CreatedAt từ RepositoryBase).
    Task<List<Apartment>> GetAllAsync(CancellationToken ct = default); // ct: hủy.

    // Phân trang + lọc route (khoảng ngày, trạng thái, số phòng chứa) + sort cột an toàn.
    Task<(List<Apartment> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page, // Trang (1-based sau Normalize).
        int pageSize, // Kích thước trang sau clamp.
        DateTime? createdAtFrom, // Lọc CreatedAt từ.
        DateTime? createdAtTo, // Lọc CreatedAt đến.
        ApartmentStatus? status, // Lọc trạng thái phòng hoặc mọi trạng thái.
        string? roomNumberContains, // Chuỗi con số phòng (trim trong repo).
        ApartmentListSort sort, // Cột + chiều sort đã parse từ route.
        CancellationToken ct = default); // ct: hủy bất đồng bộ.

    // Đọc một apartment theo Id không track.
    Task<Apartment?> GetByIdAsync(Guid id, CancellationToken ct = default); // null nếu không có.

    // Đọc tracked để cập nhật/xóa mềm.
    Task<Apartment?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default); // Tracked khi tồn tại.

    // Mọi phòng đang ở một trạng thái (ví dụ Occupied).
    Task<List<Apartment>> GetByStatusAsync(ApartmentStatus status, CancellationToken ct = default); // Lọc Status.

    // Thêm apartment mới.
    Task AddAsync(Apartment entity, CancellationToken ct = default); // Chưa SaveChanges.

    void Update(Apartment entity); // Đánh dấu Modified.

    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default); // Xóa mềm theo Id.

    Task SaveChangesAsync(CancellationToken ct = default); // Flush DbContext.

    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default); // EXISTS theo Id.
} // Kết thúc IApartmentRepository.

// Truy vấn bảng Apartment — CRUD + theo trạng thái + phân trang sort.
public sealed class ApartmentRepository : RepositoryBase<Apartment>, IApartmentRepository
{ // Mở khối ApartmentRepository.
    public ApartmentRepository(AppDbContext context) // Tiêm DbContext.
        : base(context) // Khởi tạo RepositoryBase<Apartment>.
    { // Mở khối constructor ApartmentRepository.
        // BƯỚC 1 — base(context) đã gán Context; thân rỗng — không thêm trường cục bộ.
    } // Kết thúc constructor ApartmentRepository.

    public override Task<List<Apartment>> GetAllAsync(CancellationToken ct = default) // Ủy quyền GetAll.
    { // Mở khối GetAllAsync (ApartmentRepository).
        // BƯỚC 1 — Gọi RepositoryBase.GetAllAsync (OrderBy CreatedAt, AsNoTracking).
        return base.GetAllAsync(ct); // Danh sách đầy đủ cột Apartment.
    } // Kết thúc GetAllAsync (ApartmentRepository).

    public async Task<(List<Apartment> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        ApartmentStatus? status,
        string? roomNumberContains,
        ApartmentListSort sort,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        // BƯỚC 1 — Chuẩn hóa phân trang (tránh index âm, pageSize quá lớn).
        var (p, s) = PaginationQuery.Normalize(page, pageSize); // p,s an toàn cho Skip/Take.

        // BƯỚC 2 — Bắt đầu truy vấn chỉ đọc Apartment.
        var q = Set.AsNoTracking().AsQueryable(); // IQueryable chưa SQL.

        // BƯỚC 3 — Lọc khoảng CreatedAt (extension BaseEntity).
        q = q.WhereCreatedAtRange(createdAtFrom, createdAtTo); // Có thể không thêm predicate.

        // BƯỚC 4 — Lọc trạng thái nếu client truyền status cụ thể.
        if (status is { } st) // TRƯỜNG HỢP: có lọc Status.
            q = q.Where(a => a.Status == st); // Thu hẹp theo enum.

        // BƯỚC 5 — Contains số phòng nếu chuỗi không rỗng sau Trim.
        var room = roomNumberContains?.Trim(); // null hoặc chuỗi tìm kiếm.
        if (!string.IsNullOrEmpty(room)) // TRƯỜNG HỢP: có từ khóa phòng.
            q = q.Where(a => a.RoomNumber.Contains(room)); // SQL LIKE/Contains provider.

        // BƯỚC 6 — COUNT(*) để biết tổng dòng khớp lọc (metadata TotalPages).
        var total = await q.LongCountAsync(ct); // Một round-trip count.

        // BƯỚC 7 — ORDER BY whitelist + Skip/Take đúng một trang.
        q = ApplyApartmentSort(q, sort); // Không raw string sort từ client.
        var items = await q.Skip((p - 1) * s).Take(s).ToListAsync(ct); // SELECT một trang.

        return (items, total, p, s); // Trả dữ liệu + metadata phân trang đã chuẩn hóa.
    } // Kết thúc GetPagedAsync.

    // Map cột sort (whitelist) sang OrderBy/OrderByDescending + ThenBy an toàn.
    private static IQueryable<Apartment> ApplyApartmentSort(IQueryable<Apartment> q, ApartmentListSort spec) // spec: cột + hướng.
    { // Mở khối ApplyApartmentSort.
        // BƯỚC 1 — Đọc cờ Descending một lần cho switch.
        var desc = spec.Descending; // true: giảm dần.

        // BƯỚC 2 — Switch expression: mọi nhánh trả IQueryable đã OrderBy — default CreatedAt.
        return spec.Column switch // Pattern match cột enum.
        {
            ApartmentSortColumn.Id => desc ? q.OrderByDescending(a => a.Id) : q.OrderBy(a => a.Id), // Sort theo Id.
            ApartmentSortColumn.Floor => desc ? q.OrderByDescending(a => a.Floor).ThenByDescending(a => a.RoomNumber) : q.OrderBy(a => a.Floor).ThenBy(a => a.RoomNumber), // Tầng rồi phòng.
            ApartmentSortColumn.RoomNumber => desc ? q.OrderByDescending(a => a.RoomNumber) : q.OrderBy(a => a.RoomNumber), // Số phòng.
            ApartmentSortColumn.Area => desc ? q.OrderByDescending(a => a.Area) : q.OrderBy(a => a.Area), // Diện tích.
            ApartmentSortColumn.Status => desc ? q.OrderByDescending(a => a.Status) : q.OrderBy(a => a.Status), // Trạng thái.
            ApartmentSortColumn.MaxResidents => desc ? q.OrderByDescending(a => a.MaxResidents) : q.OrderBy(a => a.MaxResidents), // Số người tối đa.
            ApartmentSortColumn.CreatedAt => desc ? q.OrderByDescending(a => a.CreatedAt) : q.OrderBy(a => a.CreatedAt), // Thời tạo.
            _ => desc ? q.OrderByDescending(a => a.CreatedAt) : q.OrderBy(a => a.CreatedAt), // Cột lạ → fallback CreatedAt.
        }; // Kết thúc switch.
    } // Kết thúc ApplyApartmentSort.

    public override Task<Apartment?> GetByIdAsync(Guid id, CancellationToken ct = default) // Đọc không track.
    { // Mở khối GetByIdAsync.
        // BƯỚC 1 — RepositoryBase: AsNoTracking FirstOrDefault theo Id.
        return base.GetByIdAsync(id, ct); // Một apartment hoặc null.
    } // Kết thúc GetByIdAsync.

    public override Task<Apartment?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) // Đọc tracked.
    { // Mở khối GetByIdTrackedAsync.
        // BƯỚC 1 — RepositoryBase: tracked FirstOrDefault theo Id.
        return base.GetByIdTrackedAsync(id, ct); // Phục vụ Update/SoftDelete.
    } // Kết thúc GetByIdTrackedAsync.

    public async Task<List<Apartment>> GetByStatusAsync(ApartmentStatus status, CancellationToken ct = default) // Lọc trạng thái.
    { // Mở khối GetByStatusAsync.
        // BƯỚC 1 — Chỉ đọc, Where Status, sắp Floor rồi RoomNumber (UX danh sách tầng).
        return await Set.AsNoTracking()
            .Where(a => a.Status == status) // Đúng trạng thái yêu cầu.
            .OrderBy(a => a.Floor).ThenBy(a => a.RoomNumber) // Ổn định theo vị trí vật lý.
            .ToListAsync(ct); // Materialize.
    } // Kết thúc GetByStatusAsync.

    public override Task AddAsync(Apartment entity, CancellationToken ct = default) // Insert pending.
    { // Mở khối AddAsync.
        // BƯỚC 1 — RepositoryBase AddAsync.
        return base.AddAsync(entity, ct); // Chưa flush DB.
    } // Kết thúc AddAsync.

    public override void Update(Apartment entity) // Modify pending.
    { // Mở khối Update.
        // BƯỚC 1 — RepositoryBase Update.
        base.Update(entity); // SaveChanges sau.
    } // Kết thúc Update.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default) // Xóa mềm theo Id.
    { // Mở khối SoftDeleteAsync.
        // BƯỚC 1 — Nạp tracked để gán cờ xóa.
        var entity = await GetByIdTrackedAsync(id, ct); // null nếu không tìm thấy.

        // TRƯỜNG HỢP: Không có apartment — báo 404 thống nhất API.
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Apartment not found."); // Lỗi domain.

        // BƯỚC 2 — Đánh dấu IsDeleted + audit; chờ SaveChanges từ tầng service.
        SoftDelete(entity, deletedBy); // RepositoryBase.
    } // Kết thúc SoftDeleteAsync.

    public override Task SaveChangesAsync(CancellationToken ct = default) // Flush.
    { // Mở khối SaveChangesAsync.
        // BƯỚC 1 — Ủy quyền Context.SaveChangesAsync.
        return base.SaveChangesAsync(ct); // Commit batch.
    } // Kết thúc SaveChangesAsync.

    public override Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) // Kiểm tra tồn tại.
    { // Mở khối ExistsAsync.
        // BƯỚC 1 — AnyAsync không materialize full row.
        return base.ExistsAsync(id, ct); // true/false.
    } // Kết thúc ExistsAsync.
} // Kết thúc ApartmentRepository.
