using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApartmentAPI.Repositories;

public interface IInvoiceRepository
{
    Task<List<Invoice>> GetAllAsync(CancellationToken ct = default);
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Invoice?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);
    Task<List<Invoice>> GetByApartmentIdAsync(Guid apartmentId, CancellationToken ct = default);
    Task AddAsync(Invoice entity, CancellationToken ct = default);
    void Update(Invoice entity);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}

public sealed class InvoiceRepository : RepositoryBase<Invoice>, IInvoiceRepository
{
    public InvoiceRepository(AppDbContext context)
        : base(context)
    {
    }

    public Task<List<Invoice>> GetAllAsync(CancellationToken ct = default) => base.GetAllAsync(ct);

    public Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default) => base.GetByIdAsync(id, ct);

    public Task<Invoice?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) => base.GetByIdTrackedAsync(id, ct);

    public async Task<List<Invoice>> GetByApartmentIdAsync(Guid apartmentId, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(i => i.ApartmentId == apartmentId)
            .OrderByDescending(i => i.Year).ThenByDescending(i => i.Month)
            .ToListAsync(ct);

    public Task AddAsync(Invoice entity, CancellationToken ct = default) => base.AddAsync(entity, ct);

    public void Update(Invoice entity) => base.Update(entity);

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    {
        var entity = await GetByIdTrackedAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Invoice not found.");
        SoftDelete(entity, deletedBy);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => base.SaveChangesAsync(ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) => base.ExistsAsync(id, ct);
}
