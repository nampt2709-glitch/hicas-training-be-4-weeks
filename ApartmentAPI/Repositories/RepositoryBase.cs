using ApartmentAPI.Data;
using Microsoft.EntityFrameworkCore;

using ApartmentAPI.Entities;

namespace ApartmentAPI.Repositories;

// Lớp nền CRUD + soft delete mềm cho entity kế thừa BaseEntity.
public abstract class RepositoryBase<TEntity> where TEntity : BaseEntity
{
    protected readonly AppDbContext Context;

    protected RepositoryBase(AppDbContext context)
    {
        Context = context;
    }

    protected DbSet<TEntity> Set => Context.Set<TEntity>();

    public virtual async Task<List<TEntity>> GetAllAsync(CancellationToken ct = default) =>
        await Set.AsNoTracking().OrderBy(e => e.CreatedAt).ToListAsync(ct);

    public virtual Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Set.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public virtual Task<TEntity?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(e => e.Id == id, ct);

    public virtual async Task AddAsync(TEntity entity, CancellationToken ct = default) =>
        await Set.AddAsync(entity, ct);

    public virtual void Update(TEntity entity) => Set.Update(entity);

    public virtual void SoftDelete(TEntity entity, string? deletedBy)
    {
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = deletedBy;
        Set.Update(entity);
    }

    public virtual Task SaveChangesAsync(CancellationToken ct = default) =>
        Context.SaveChangesAsync(ct);

    public virtual Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) =>
        Set.AsNoTracking().AnyAsync(e => e.Id == id, ct);
}
