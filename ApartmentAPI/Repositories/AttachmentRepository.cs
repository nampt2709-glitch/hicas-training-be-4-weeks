using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApartmentAPI.Repositories;

public interface IAttachmentRepository
{
    Task<List<Attachment>> GetAllAsync(CancellationToken ct = default);
    Task<Attachment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Attachment?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);
    Task<List<Attachment>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<List<Attachment>> GetByFeedbackIdAsync(Guid feedbackId, CancellationToken ct = default);
    Task<List<Attachment>> GetByPostIdAsync(Guid postId, CancellationToken ct = default);
    Task<List<Attachment>> GetByScopeAsync(AttachmentScope scope, CancellationToken ct = default);
    Task AddAsync(Attachment entity, CancellationToken ct = default);
    void Update(Attachment entity);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}

public sealed class AttachmentRepository : RepositoryBase<Attachment>, IAttachmentRepository
{
    public AttachmentRepository(AppDbContext context)
        : base(context)
    {
    }

    public Task<List<Attachment>> GetAllAsync(CancellationToken ct = default) => base.GetAllAsync(ct);

    public Task<Attachment?> GetByIdAsync(Guid id, CancellationToken ct = default) => base.GetByIdAsync(id, ct);

    public Task<Attachment?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) => base.GetByIdTrackedAsync(id, ct);

    public async Task<List<Attachment>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(a => a.UserId == userId).ToListAsync(ct);

    public async Task<List<Attachment>> GetByFeedbackIdAsync(Guid feedbackId, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(a => a.FeedbackId == feedbackId).ToListAsync(ct);

    public async Task<List<Attachment>> GetByPostIdAsync(Guid postId, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(a => a.PostId == postId).ToListAsync(ct);

    public async Task<List<Attachment>> GetByScopeAsync(AttachmentScope scope, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(a => a.Scope == scope).ToListAsync(ct);

    public Task AddAsync(Attachment entity, CancellationToken ct = default) => base.AddAsync(entity, ct);

    public void Update(Attachment entity) => base.Update(entity);

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    {
        var entity = await GetByIdTrackedAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Attachment not found.");
        SoftDelete(entity, deletedBy);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => base.SaveChangesAsync(ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) => base.ExistsAsync(id, ct);
}
