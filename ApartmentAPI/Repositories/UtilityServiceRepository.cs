using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApartmentAPI.Repositories;

public interface IUtilityServiceRepository
{
    Task<List<UtilityService>> GetAllAsync(CancellationToken ct = default);
    Task<UtilityService?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UtilityService?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);
    Task<List<UtilityService>> GetActiveAsync(CancellationToken ct = default);
    Task AddAsync(UtilityService entity, CancellationToken ct = default);
    void Update(UtilityService entity);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}

public sealed class UtilityServiceRepository : RepositoryBase<UtilityService>, IUtilityServiceRepository
{
    public UtilityServiceRepository(AppDbContext context)
        : base(context)
    {
    }

    public Task<List<UtilityService>> GetAllAsync(CancellationToken ct = default) => base.GetAllAsync(ct);

    public Task<UtilityService?> GetByIdAsync(Guid id, CancellationToken ct = default) => base.GetByIdAsync(id, ct);

    public Task<UtilityService?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) => base.GetByIdTrackedAsync(id, ct);

    public async Task<List<UtilityService>> GetActiveAsync(CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync(ct);

    public Task AddAsync(UtilityService entity, CancellationToken ct = default) => base.AddAsync(entity, ct);

    public void Update(UtilityService entity) => base.Update(entity);

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    {
        var entity = await GetByIdTrackedAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Utility service not found.");
        SoftDelete(entity, deletedBy);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => base.SaveChangesAsync(ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) => base.ExistsAsync(id, ct);
}
