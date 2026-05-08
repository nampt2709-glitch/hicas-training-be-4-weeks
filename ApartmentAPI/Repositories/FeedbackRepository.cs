using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApartmentAPI.Repositories;

public interface IFeedbackRepository
{
    Task<List<Feedback>> GetAllAsync(CancellationToken ct = default);
    Task<Feedback?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Feedback?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);
    Task<List<Feedback>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<List<Feedback>> GetRootsAsync(CancellationToken ct = default);
    Task AddAsync(Feedback entity, CancellationToken ct = default);
    void Update(Feedback entity);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}

public sealed class FeedbackRepository : RepositoryBase<Feedback>, IFeedbackRepository
{
    public FeedbackRepository(AppDbContext context)
        : base(context)
    {
    }

    public Task<List<Feedback>> GetAllAsync(CancellationToken ct = default) => base.GetAllAsync(ct);

    public Task<Feedback?> GetByIdAsync(Guid id, CancellationToken ct = default) => base.GetByIdAsync(id, ct);

    public Task<Feedback?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) => base.GetByIdTrackedAsync(id, ct);

    public async Task<List<Feedback>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(f => f.UserId == userId).OrderByDescending(f => f.CreatedAt).ToListAsync(ct);

    public async Task<List<Feedback>> GetRootsAsync(CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(f => f.ParentId == null).OrderByDescending(f => f.IsPinned).ThenBy(f => f.CreatedAt).ToListAsync(ct);

    public Task AddAsync(Feedback entity, CancellationToken ct = default) => base.AddAsync(entity, ct);

    public void Update(Feedback entity) => base.Update(entity);

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    {
        var entity = await GetByIdTrackedAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Feedback not found.");
        SoftDelete(entity, deletedBy);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => base.SaveChangesAsync(ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) => base.ExistsAsync(id, ct);
}
