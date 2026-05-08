using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApartmentAPI.Repositories;

public interface IRefreshTokenRepository
{
    Task<List<RefreshToken>> GetAllAsync(CancellationToken ct = default);
    Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RefreshToken?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);
    Task<List<RefreshToken>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(RefreshToken entity, CancellationToken ct = default);
    void Update(RefreshToken entity);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}

public sealed class RefreshTokenRepository : RepositoryBase<RefreshToken>, IRefreshTokenRepository
{
    public RefreshTokenRepository(AppDbContext context)
        : base(context)
    {
    }

    public Task<List<RefreshToken>> GetAllAsync(CancellationToken ct = default) => base.GetAllAsync(ct);

    public Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken ct = default) => base.GetByIdAsync(id, ct);

    public Task<RefreshToken?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) => base.GetByIdTrackedAsync(id, ct);

    public async Task<List<RefreshToken>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(t => t.UserId == userId).ToListAsync(ct);

    public Task AddAsync(RefreshToken entity, CancellationToken ct = default) => base.AddAsync(entity, ct);

    public void Update(RefreshToken entity) => base.Update(entity);

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    {
        var entity = await GetByIdTrackedAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Refresh token not found.");
        SoftDelete(entity, deletedBy);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => base.SaveChangesAsync(ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) => base.ExistsAsync(id, ct);
}
