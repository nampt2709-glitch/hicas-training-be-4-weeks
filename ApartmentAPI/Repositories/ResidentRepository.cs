using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApartmentAPI.Repositories;

public interface IResidentRepository
{
    Task<List<Resident>> GetAllAsync(CancellationToken ct = default);
    Task<Resident?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Resident?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);
    Task<List<Resident>> GetByApartmentIdAsync(Guid apartmentId, CancellationToken ct = default);
    Task AddAsync(Resident entity, CancellationToken ct = default);
    void Update(Resident entity);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}

public sealed class ResidentRepository : RepositoryBase<Resident>, IResidentRepository
{
    public ResidentRepository(AppDbContext context)
        : base(context)
    {
    }

    public Task<List<Resident>> GetAllAsync(CancellationToken ct = default) => base.GetAllAsync(ct);

    public Task<Resident?> GetByIdAsync(Guid id, CancellationToken ct = default) => base.GetByIdAsync(id, ct);

    public Task<Resident?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) => base.GetByIdTrackedAsync(id, ct);

    public async Task<List<Resident>> GetByApartmentIdAsync(Guid apartmentId, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(r => r.ApartmentId == apartmentId).OrderBy(r => r.FullName).ToListAsync(ct);

    public Task AddAsync(Resident entity, CancellationToken ct = default) => base.AddAsync(entity, ct);

    public void Update(Resident entity) => base.Update(entity);

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    {
        var entity = await GetByIdTrackedAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Resident not found.");
        SoftDelete(entity, deletedBy);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => base.SaveChangesAsync(ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) => base.ExistsAsync(id, ct);
}
