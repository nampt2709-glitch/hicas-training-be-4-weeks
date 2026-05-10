using ApartmentAPI.DTOs; // PagedResult.
using ApartmentAPI.V1.DTOs; // FeedbackDto, Create/Update.
using ApartmentAPI.Entities; // Feedback, User.
using ApartmentAPI.Repositories; // IFeedbackRepository.
using AutoMapper; // IMapper.
using Microsoft.AspNetCore.Identity; // UserManager.

namespace ApartmentAPI.Services;

// Nghiệp vụ phản hồi: danh sách phân trang + cache khi không filter, CRUD.
public interface IFeedbackService
{
    Task<PagedResult<FeedbackDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        bool rootsOnly,
        string? contentContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default);

    Task<FeedbackDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FeedbackDto> CreateAsync(CreateFeedbackDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateFeedbackDto dto, CancellationToken ct = default);
    // Admin: PUT .../feedbacks/{id}/admin — đổi UserId, ParentId, cờ; chặn ParentId tạo chu trình (giống CommentAPI admin).
    Task UpdateAsAdminAsync(Guid id, AdminUpdateFeedbackDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

// CRUD Feedback: UserId và ParentId (nếu có) phải hợp lệ.
public sealed class FeedbackService : IFeedbackService
{
    private readonly IFeedbackRepository _repository; // Truy vấn Feedback + Exists parent.
    private readonly UserManager<User> _users; // Kiểm tra UserId.
    private readonly IMapper _mapper; // Map entity ↔ DTO.
    private readonly IEntityResponseCache _cache; // Cache chi tiết/trang.
    private readonly ICacheListEpochStore _listEpoch; // Epoch danh sách feedback.

    public FeedbackService(
        IFeedbackRepository repository,
        UserManager<User> users,
        IMapper mapper,
        IEntityResponseCache cache,
        ICacheListEpochStore listEpoch)
    { // Mở khối constructor.
        _repository = repository;
        _users = users;
        _mapper = mapper;
        _cache = cache;
        _listEpoch = listEpoch;
    } // Kết thúc constructor.

    // BFS trên danh sách phẳng (Id, ParentId): tập Id gồm root và mọi hậu duệ — chặn đặt cha trùng cây con (chu trình khi duyệt).
    private static HashSet<Guid> BuildFeedbackSubtreeIdSet(IReadOnlyList<(Guid Id, Guid? ParentId)> rows, Guid rootId)
    { // Mở khối BuildFeedbackSubtreeIdSet.
        var s = new HashSet<Guid> { rootId };
        var q = new Queue<Guid>();
        q.Enqueue(rootId);
        while (q.Count > 0)
        { // Mở vòng BFS.
            var u = q.Dequeue();
            foreach (var (childId, pid) in rows)
            { // Duyệt mọi cạnh cha → con.
                if (pid == u && s.Add(childId))
                    q.Enqueue(childId);
            }
        } // Kết thúc BFS.
        return s;
    } // Kết thúc BuildFeedbackSubtreeIdSet.

    private static bool HasListFilter(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        bool rootsOnly,
        string? contentContains) =>
        createdAtFrom.HasValue
        || createdAtTo.HasValue
        || userId.HasValue
        || rootsOnly
        || !string.IsNullOrWhiteSpace(contentContains);

    public async Task<PagedResult<FeedbackDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        bool rootsOnly,
        string? contentContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        var sortSpec = ListSortParsing.ParseFeedbackSort(sort, sortDir);

        if (!HasListFilter(createdAtFrom, createdAtTo, userId, rootsOnly, contentContains))
        {
            var epoch = await _listEpoch.GetFeedbacksListEpochAsync(ct);
            var key = EntityCacheKeys.FeedbacksPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            var cached = await _cache.GetJsonAsync<PagedResult<FeedbackDto>>(key, ct);
            if (cached is not null)
                return cached;
        }

        var (items, total, p, s) = await _repository.GetPagedAsync(
            page, pageSize, createdAtFrom, createdAtTo, userId, rootsOnly, contentContains, sortSpec, ct);

        var dtos = _mapper.Map<List<FeedbackDto>>(items);
        var result = new PagedResult<FeedbackDto> { Items = dtos, Page = p, PageSize = s, TotalCount = total };

        if (!HasListFilter(createdAtFrom, createdAtTo, userId, rootsOnly, contentContains))
        {
            var epoch = await _listEpoch.GetFeedbacksListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.FeedbacksPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            await _cache.SetJsonAsync(cacheKey, result, ct);
        }

        return result;
    } // Kết thúc GetPagedAsync.

    public async Task<FeedbackDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        var key = EntityCacheKeys.Feedback(id);
        var cached = await _cache.GetJsonAsync<FeedbackDto>(key, ct);
        if (cached is not null)
            return cached;

        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Feedback not found.");
        var dto = _mapper.Map<FeedbackDto>(entity);
        await _cache.SetJsonAsync(key, dto, ct);
        return dto;
    } // Kết thúc GetByIdAsync.

    public async Task<FeedbackDto> CreateAsync(CreateFeedbackDto dto, CancellationToken ct = default)
    { // Mở khối CreateAsync.
        // BƯỚC 1 — User tồn tại.
        if (await _users.FindByIdAsync(dto.UserId.ToString()) is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");
        // BƯỚC 2 — Parent (nếu có) phải tồn tại để tránh FK/cây hỏng.
        if (dto.ParentId is { } pId && !await _repository.ExistsAsync(pId, ct))
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Parent feedback not found.");

        var entity = _mapper.Map<Feedback>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        var dtoOut = _mapper.Map<FeedbackDto>(entity);
        await _cache.SetJsonAsync(EntityCacheKeys.Feedback(entity.Id), dtoOut, ct);
        await _listEpoch.InvalidateFeedbacksListsAsync(ct);
        return dtoOut;
    } // Kết thúc CreateAsync.

    public async Task UpdateAsync(Guid id, UpdateFeedbackDto dto, CancellationToken ct = default)
    { // Mở khối UpdateAsync.
        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Feedback not found.");
        _mapper.Map(dto, tracked);
        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
        await _cache.RemoveAsync(EntityCacheKeys.Feedback(id), ct);
        await _listEpoch.InvalidateFeedbacksListsAsync(ct);
    } // Kết thúc UpdateAsync.

    // Admin — cập nhật đầy đủ quan hệ cây và tác giả; chặn gán cha = chính nó hoặc hậu duệ (chu trình vô hạn khi đọc cây).
    public async Task UpdateAsAdminAsync(Guid id, AdminUpdateFeedbackDto dto, CancellationToken ct = default)
    { // Mở khối UpdateAsAdminAsync.
        // BƯỚC 1 — User đích phải tồn tại trong Identity.
        if (await _users.FindByIdAsync(dto.UserId.ToString()) is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");

        // BƯỚC 2 — Nạp feedback tracked; không có → 404.
        var root = await _repository.GetByIdTrackedAsync(id, ct);
        if (root is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Feedback not found.");

        // BƯỚC 3 — Toàn bộ cặp cha–con hiện có (bản ghi chưa soft delete) để BFS subtree gốc id.
        var flatLinks = await _repository.GetAllIdParentPairsAsync(ct);
        var subtree = BuildFeedbackSubtreeIdSet(flatLinks, root.Id);

        // BƯỚC 4 — Kiểm tra ParentId mới (nếu có): không tự tham chiếu, cha không nằm trong subtree, cha tồn tại.
        if (dto.ParentId is { } newParentId)
        { // Mở nhánh reparent.
            if (newParentId == id)
                throw ApiException.BadRequest(ApiErrorCodes.FeedbackReparentCausesCycle, ApiMessages.FeedbackReparentCausesCycle);
            if (subtree.Contains(newParentId))
                throw ApiException.BadRequest(ApiErrorCodes.FeedbackReparentCausesCycle, ApiMessages.FeedbackReparentCausesCycle);
            if (!await _repository.ExistsAsync(newParentId, ct))
                throw ApiException.BadRequest(ApiErrorCodes.Validation, "Parent feedback not found.");
        } // Kết thúc nhánh reparent.

        // BƯỚC 5 — Gán scalar và quan hệ, lưu DB, hủy cache danh sách/chi tiết.
        root.Content = dto.Content;
        root.IsResolved = dto.IsResolved;
        root.IsPinned = dto.IsPinned;
        root.UserId = dto.UserId;
        root.ParentId = dto.ParentId;
        _repository.Update(root);
        await _repository.SaveChangesAsync(ct);
        await _cache.RemoveAsync(EntityCacheKeys.Feedback(id), ct);
        await _listEpoch.InvalidateFeedbacksListsAsync(ct);
    } // Kết thúc UpdateAsAdminAsync.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
        await _cache.RemoveAsync(EntityCacheKeys.Feedback(id), ct);
        await _listEpoch.InvalidateFeedbacksListsAsync(ct);
    } // Kết thúc SoftDeleteAsync.
}
