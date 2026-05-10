using CommentAPI.Data; // AppDbContext — DbSet và SaveChangesAsync.
using Microsoft.EntityFrameworkCore; // DbSet cụ thể, EF.Property, mở rộng IQueryable (Where, FirstOrDefaultAsync…).

namespace CommentAPI.Repositories;

// CRUD generic tối thiểu + ApplyCreatedAtRange — repository Post/User/Comment kế thừa và bổ sung truy vấn.
public abstract class RepositoryBase<TEntity> where TEntity : class
{ // Mở khối RepositoryBase generic theo loại entity.
    // DbContext theo scope request — một instance chung trong một HTTP request.
    protected readonly AppDbContext Context;

    // BƯỚC 1 — Lưu tham chiếu AppDbContext để Set entity, SaveChanges, v.v.
    protected RepositoryBase(AppDbContext context)
    { // Mở constructor.
        Context = context; // Trường protected cho lớp con.
    } // Kết thúc constructor.

    // Shortcut tới DbSet của TEntity trong context hiện tại.
    protected DbSet<TEntity> Set => Context.Set<TEntity>();

    // Đọc một entity theo shadow property Id (chuẩn Guid) — không bắt buộc property C# tên Id.
    public virtual Task<TEntity?> GetByIdAsync(Guid id) =>
        Set.FirstOrDefaultAsync(x => EF.Property<Guid>(x, "Id") == id);

    // Đánh dấu Added — chờ SaveChangesAsync.
    public virtual async Task AddAsync(TEntity entity)
    { // Mở AddAsync.
        await Set.AddAsync(entity);
    } // Kết thúc AddAsync.

    // Đánh dấu Modified — cập nhật khi SaveChanges.
    public virtual void Update(TEntity entity)
    { // Mở Update.
        Set.Update(entity);
    } // Kết thúc Update.

    // Đánh dấu Deleted (hoặc soft-delete nếu có interceptor).
    public virtual void Remove(TEntity entity)
    { // Mở Remove.
        Set.Remove(entity);
    } // Kết thúc Remove.

    // Kiểm tra tồn tại không track — chi phí nhẹ cho validate FK.
    public virtual Task<bool> ExistsAsync(Guid id) =>
        Set.AsNoTracking().AnyAsync(x => EF.Property<Guid>(x, "Id") == id);

    // Flush thay đổi một round-trip DB.
    public virtual Task SaveChangesAsync() =>
        Context.SaveChangesAsync();

    // Gắn WHERE CreatedAt từ mốc đến mốc (inclusive: cận dưới và cận trên); cả hai null thì query giữ nguyên.
    protected static IQueryable<TEntity> ApplyCreatedAtRange(
        IQueryable<TEntity> query,
        DateTime? createdAtFrom,
        DateTime? createdAtTo)
    { // Mở ApplyCreatedAtRange.
        if (createdAtFrom is { } from)
        {
            query = query.Where(x => EF.Property<DateTime>(x, "CreatedAt") >= from);
        }

        if (createdAtTo is { } to)
        {
            query = query.Where(x => EF.Property<DateTime>(x, "CreatedAt") <= to);
        }

        return query;
    } // Kết thúc ApplyCreatedAtRange.
} // Kết thúc lớp RepositoryBase.
