using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApartmentAPI.Repositories;

public interface IInvoiceItemRepository
{
    Task<List<InvoiceItem>> GetAllAsync(CancellationToken ct = default);
    Task<InvoiceItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<InvoiceItem?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);
    Task<List<InvoiceItem>> GetByInvoiceIdAsync(Guid invoiceId, CancellationToken ct = default);
    Task AddAsync(InvoiceItem entity, CancellationToken ct = default);
    void Update(InvoiceItem entity);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}

public sealed class InvoiceItemRepository : RepositoryBase<InvoiceItem>, IInvoiceItemRepository
{
    public InvoiceItemRepository(AppDbContext context)
        : base(context)
    {
    }

    public Task<List<InvoiceItem>> GetAllAsync(CancellationToken ct = default) => base.GetAllAsync(ct);

    public Task<InvoiceItem?> GetByIdAsync(Guid id, CancellationToken ct = default) => base.GetByIdAsync(id, ct);

    public Task<InvoiceItem?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) => base.GetByIdTrackedAsync(id, ct);

    public async Task<List<InvoiceItem>> GetByInvoiceIdAsync(Guid invoiceId, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(i => i.InvoiceId == invoiceId).ToListAsync(ct);

    public Task AddAsync(InvoiceItem entity, CancellationToken ct = default) => base.AddAsync(entity, ct);

    public void Update(InvoiceItem entity) => base.Update(entity);

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    {
        var entity = await GetByIdTrackedAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Invoice item not found.");
        SoftDelete(entity, deletedBy);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => base.SaveChangesAsync(ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) => base.ExistsAsync(id, ct);
}
