using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApartmentAPI.Repositories;

// Hợp đồng repository Apartment: CRUD + lọc theo trạng thái phòng.
public interface IApartmentRepository
{
    Task<List<Apartment>> GetAllAsync(CancellationToken ct = default);
    Task<Apartment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Apartment?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);
    Task<List<Apartment>> GetByStatusAsync(ApartmentStatus status, CancellationToken ct = default);
    Task AddAsync(Apartment entity, CancellationToken ct = default);
    void Update(Apartment entity);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}

// Truy vấn bảng Apartment — CRUD + theo trạng thái.
public sealed class ApartmentRepository : RepositoryBase<Apartment>, IApartmentRepository
{
    public ApartmentRepository(AppDbContext context)
        : base(context)
    {
    }

    public Task<List<Apartment>> GetAllAsync(CancellationToken ct = default) =>
        base.GetAllAsync(ct);

    public Task<Apartment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        base.GetByIdAsync(id, ct);

    public Task<Apartment?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) =>
        base.GetByIdTrackedAsync(id, ct);

    public async Task<List<Apartment>> GetByStatusAsync(ApartmentStatus status, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(a => a.Status == status)
            .OrderBy(a => a.Floor).ThenBy(a => a.RoomNumber)
            .ToListAsync(ct);

    public Task AddAsync(Apartment entity, CancellationToken ct = default) =>
        base.AddAsync(entity, ct);

    public void Update(Apartment entity) => base.Update(entity);

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    {
        var entity = await GetByIdTrackedAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Apartment not found.");
        SoftDelete(entity, deletedBy);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => base.SaveChangesAsync(ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) => base.ExistsAsync(id, ct);
}
