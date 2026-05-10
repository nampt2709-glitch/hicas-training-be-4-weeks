using ApartmentAPI.Data; // DbContext ứng dụng (AppDbContext).
using Microsoft.EntityFrameworkCore; // EF Core: DbSet, AsNoTracking, SaveChangesAsync, AnyAsync, v.v.

using ApartmentAPI.Entities; // BaseEntity: Id, CreatedAt, IsDeleted, v.v.

namespace ApartmentAPI.Repositories;

// Lớp nền CRUD + xóa mềm cho entity kế thừa BaseEntity — DbSet chung và SaveChanges một chỗ.
public abstract class RepositoryBase<TEntity> where TEntity : BaseEntity
{ // Mở khối RepositoryBase<TEntity>.
    // Ngữ cảnh EF: mọi truy vấn/ghi trong repository con dùng cùng instance (đồng bộ transaction).
    protected readonly AppDbContext Context; // Tham chiếu AppDbContext được tiêm qua constructor.

    protected RepositoryBase(AppDbContext context) // Constructor protected: chỉ lớp con khởi tạo.
    { // Mở khối constructor RepositoryBase.
        // BƯỚC 1 — Lưu DbContext để property Set và SaveChangesAsync hoạt động thống nhất.
        Context = context; // Gán context nền cho mọi thao tác repository.
    } // Kết thúc constructor RepositoryBase.

    // Truy cập DbSet<TEntity> — EF ánh xạ bảng tương ứng với TEntity.
    protected DbSet<TEntity> Set => Context.Set<TEntity>(); // DbSet generic theo TEntity.

    // Đọc toàn bộ bản ghi (không track) — sắp theo CreatedAt tăng dần.
    public virtual async Task<List<TEntity>> GetAllAsync(CancellationToken ct = default) // ct: hủy tác vụ bất đồng bộ.
    { // Mở khối GetAllAsync.
        // BƯỚC 1 — AsNoTracking (chỉ đọc) + OrderBy CreatedAt + materialize một round-trip SELECT.
        return await Set.AsNoTracking().OrderBy(e => e.CreatedAt).ToListAsync(ct); // Danh sách entity không tracked.
    } // Kết thúc GetAllAsync.

    // Đọc một bản ghi theo Id (không track) — null nếu không tồn tại.
    public virtual Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) // id: khóa chính.
    { // Mở khối GetByIdAsync.
        // BƯỚC 1 — FirstOrDefault trên AsNoTracking: tối đa một dòng hoặc null.
        return Set.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct); // Không Modify/delete qua entity này.
    } // Kết thúc GetByIdAsync.

    // Đọc một bản ghi theo Id có track — phụ vụ Update/SoftDelete sau khi nạp.
    public virtual Task<TEntity?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) // id: khóa chính.
    { // Mở khối GetByIdTrackedAsync.
        // BƯỚC 1 — Không AsNoTracking: EF theo dõi thay đổi cho SaveChanges.
        return Set.FirstOrDefaultAsync(e => e.Id == id, ct); // Entity tracked nếu tìm thấy.
    } // Kết thúc GetByIdTrackedAsync.

    // Thêm entity mới — chưa ghi DB cho đến SaveChangesAsync.
    public virtual async Task AddAsync(TEntity entity, CancellationToken ct = default) // entity: bản ghi chưa có trong DB.
    { // Mở khối AddAsync.
        // BƯỚC 1 — Đánh dấu Added trong change tracker; persist khi gọi SaveChanges.
        await Set.AddAsync(entity, ct); // Insert khi SaveChanges.
    } // Kết thúc AddAsync.

    // Đánh dấu entity đã chỉnh sửa — dùng khi đã tracked hoặc attach tay (ở đây gọi Update an toàn).
    public virtual void Update(TEntity entity) // entity: trạng thái Modified sau khi gán thuộc tính.
    { // Mở khối Update.
        // BƯỚC 1 — Set.Update để EF coi toàn bộ scalar properties là đã đổi (trừ key).
        Set.Update(entity); // Modified trong change tracker.
    } // Kết thúc Update.

    // Xóa mềm: cờ IsDeleted + audit DeletedAt/DeletedBy rồi Update.
    public virtual void SoftDelete(TEntity entity, string? deletedBy) // deletedBy: ai thực hiện (nullable).
    { // Mở khối SoftDelete.
        // BƯỚC 1 — Gán cờ và mốc thời gian xóa logic; không DELETE vật lý.
        entity.IsDeleted = true; // Lọc global query có thể ẩn bản ghi đã xóa.
        entity.DeletedAt = DateTime.UtcNow; // Chuẩn UTC để nhất quán server.
        entity.DeletedBy = deletedBy; // Ghi nhận user/service nếu có.
        // BƯỚC 2 — Persist trạng thái Modified khi SaveChanges.
        Set.Update(entity); // Đồng bộ cờ với DB ở lần SaveChanges kế tiếp.
    } // Kết thúc SoftDelete.

    // Ghi mọi thay đổi đang treo (Add/Update/Delete) xuống DB.
    public virtual Task SaveChangesAsync(CancellationToken ct = default) // ct: hủy.
    { // Mở khối SaveChangesAsync.
        // BƯỚC 1 — EF Core flush change tracker → SQL (transaction mặc định một batch).
        return Context.SaveChangesAsync(ct); // Số dòng ảnh hưởng ở kết quả Task<int> (bỏ qua ở đây).
    } // Kết thúc SaveChangesAsync.

    // Kiểm tra tồn tại Id (chỉ đọc, không materialize entity đầy đủ).
    public virtual Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) // id: khóa cần kiểm tra.
    { // Mở khối ExistsAsync.
        // BƯỚC 1 — AnyAsync trên AsNoTracking: EXISTS trong SQL, hiệu năng tốt.
        return Set.AsNoTracking().AnyAsync(e => e.Id == id, ct); // true nếu có ít nhất một dòng.
    } // Kết thúc ExistsAsync.
} // Kết thúc RepositoryBase<TEntity>.
