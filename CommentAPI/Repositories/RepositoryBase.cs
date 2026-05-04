using CommentAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Repositories;

// Lớp cơ sở: CRUD tối thiểu + lọc CreatedAt dùng chung cho repository kế thừa (Post, User, Comment, …).
public abstract class RepositoryBase<TEntity> where TEntity : class
{
    // DbContext scoped do DI cấp — mọi repository con dùng chung một instance trong một request.
    protected readonly AppDbContext Context;

    // BƯỚC 1: Lưu tham chiếu DbContext để các phương thức virtual CRUD truy cập Set<T> và SaveChanges.
    protected RepositoryBase(AppDbContext context)
    {
        Context = context; // Gán vào trường protected để lớp con chỉ đọc, không tự tạo context.
    }

    // Truy cập DbSet<TEntity> qua API chung của EF — tránh lặp Context.Set<TEntity>() ở mọi chỗ.
    protected DbSet<TEntity> Set => Context.Set<TEntity>();

    // Mục đích: đọc một entity theo khóa Guid tên shadow property "Id" (chuẩn hóa cho mọi entity có cột Id).
    // TRƯỜNG HỢP: Không có bản ghi → null; có → entity (có thể tracked tùy caller đã AsNoTracking hay chưa).
    public virtual Task<TEntity?> GetByIdAsync(Guid id) =>
        Set.FirstOrDefaultAsync(x => EF.Property<Guid>(x, "Id") == id); // So khóa bằng EF.Property để không phụ thuộc tên property C#.

    // Mục đích: đánh dấu entity mới — chưa ghi DB cho đến SaveChangesAsync.
    public virtual async Task AddAsync(TEntity entity)
    {
        await Set.AddAsync(entity); // Đưa vào tracker trạng thái Added.
    }

    // Mục đích: đánh dấu entity đã tồn tại là Modified — cập nhật khi SaveChanges.
    public virtual void Update(TEntity entity)
    {
        Set.Update(entity); // Toàn bộ property có thể được ghi lại tùy cấu hình change tracking.
    }

    // Mục đích: đánh dấu xóa (hoặc soft-delete nếu global filter / interceptor xử lý).
    public virtual void Remove(TEntity entity)
    {
        Set.Remove(entity); // Trạng thái Deleted trong change tracker.
    }

    // Mục đích: kiểm tra tồn tại theo Id mà không track — dùng cho validate FK nhanh.
    public virtual Task<bool> ExistsAsync(Guid id) =>
        Set.AsNoTracking().AnyAsync(x => EF.Property<Guid>(x, "Id") == id); // Any trả bool; không materialize entity.

    // Mục đích: flush mọi thay đổi (Insert/Update/Delete) xuống database trong một transaction mặc định của EF.
    public virtual Task SaveChangesAsync() =>
        Context.SaveChangesAsync(); // Trả Task để caller await.

    // Mục đích: áp điều kiện CreatedAt inclusive trên IQueryable — entity phải có cột CreatedAt map được qua EF.Property.
    // BƯỚC 1: Nếu có createdAtFrom → thêm Where CreatedAt >= from.
    // BƯỚC 2: Nếu có createdAtTo → thêm Where CreatedAt <= to.
    // TRƯỜNG HỢP: Cả hai null → trả query gốc không đổi.
    protected static IQueryable<TEntity> ApplyCreatedAtRange(
        IQueryable<TEntity> query,
        DateTime? createdAtFrom,
        DateTime? createdAtTo)
    {
        if (createdAtFrom is { } from) // Pattern: có giá trị cận dưới.
        {
            query = query.Where(x => EF.Property<DateTime>(x, "CreatedAt") >= from); // Lọc từ mốc thời gian (bao gồm mốc).
        }

        if (createdAtTo is { } to) // Pattern: có giá trị cận trên.
        {
            query = query.Where(x => EF.Property<DateTime>(x, "CreatedAt") <= to); // Lọc đến mốc (bao gồm mốc).
        }

        return query; // IQueryable chưa thực thi SQL — caller tiếp tục OrderBy/Skip/Take.
    }
}
