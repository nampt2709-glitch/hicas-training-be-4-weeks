using CommentAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Repositories;

// Base repository dùng chung CRUD cơ bản cho entity có khóa Guid "Id".
public abstract class RepositoryBase<TEntity> where TEntity : class
{
    protected readonly AppDbContext Context;

    protected RepositoryBase(AppDbContext context)
    {
        Context = context;
    }

    protected DbSet<TEntity> Set => Context.Set<TEntity>();

    // Dùng EF.Property để áp dụng cho mọi entity có cột Id kiểu Guid.
    public virtual Task<TEntity?> GetByIdAsync(Guid id) =>
        Set.FirstOrDefaultAsync(x => EF.Property<Guid>(x, "Id") == id);

    public virtual async Task AddAsync(TEntity entity)
    {
        await Set.AddAsync(entity);
    }

    public virtual void Update(TEntity entity)
    {
        Set.Update(entity);
    }

    public virtual void Remove(TEntity entity)
    {
        Set.Remove(entity);
    }

    public virtual Task<bool> ExistsAsync(Guid id) =>
        Set.AsNoTracking().AnyAsync(x => EF.Property<Guid>(x, "Id") == id);

    public virtual Task SaveChangesAsync() =>
        Context.SaveChangesAsync();

    // Filter CreatedAt dùng chung cho entity có cột DateTime CreatedAt.
    protected static IQueryable<TEntity> ApplyCreatedAtRange(
        IQueryable<TEntity> query,
        DateTime? createdAtFrom,
        DateTime? createdAtTo)
    {
        if (createdAtFrom is { } from)
        {
            query = query.Where(x => EF.Property<DateTime>(x, "CreatedAt") >= from);
        }

        if (createdAtTo is { } to)
        {
            query = query.Where(x => EF.Property<DateTime>(x, "CreatedAt") <= to);
        }

        return query;
    }
}
