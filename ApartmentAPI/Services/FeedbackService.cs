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

    // CTE đệ quy + flatten (mirror CommentAPI /comments/cte, /tree/cte, /tree/cte/flatten).
    Task<PagedResult<FeedbackCteDto>> GetCteFlatRoutePagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        string? contentContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default);

    Task<PagedResult<FeedbackTreeCteDto>> GetTreeCteRoutePagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        string? contentContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default);

    Task<PagedResult<FeedbackFlattenCteDto>> GetTreeCteFlattenRoutePagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        string? contentContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default);
}

// CRUD Feedback: UserId và ParentId (nếu có) phải hợp lệ.
public sealed class FeedbackService : ServiceBase, IFeedbackService
{
    private readonly IFeedbackRepository _repository; // Truy vấn Feedback + Exists parent.
    private readonly UserManager<User> _users; // Kiểm tra UserId.
    private readonly IMapper _mapper; // Map entity ↔ DTO.

    public FeedbackService(
        IFeedbackRepository repository,
        UserManager<User> users,
        IMapper mapper,
        IEntityResponseCache cache,
        ICacheListEpochStore listEpoch)
        : base(cache, listEpoch)
    { // Mở khối constructor.
        _repository = repository;
        _users = users;
        _mapper = mapper;
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
        HasCreatedAtFilter(createdAtFrom, createdAtTo)
        || userId.HasValue
        || rootsOnly
        || HasTextFilter(contentContains);

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
            var epoch = await ListEpoch.GetFeedbacksListEpochAsync(ct);
            var key = EntityCacheKeys.FeedbacksPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            var cached = await Cache.GetJsonAsync<PagedResult<FeedbackDto>>(key, ct);
            if (cached is not null)
                return cached;
        }

        var (items, total, p, s) = await _repository.GetPagedAsync(
            page, pageSize, createdAtFrom, createdAtTo, userId, rootsOnly, contentContains, sortSpec, ct);

        var dtos = _mapper.Map<List<FeedbackDto>>(items);
        var result = new PagedResult<FeedbackDto> { Items = dtos, Page = p, PageSize = s, TotalCount = total };

        if (!HasListFilter(createdAtFrom, createdAtTo, userId, rootsOnly, contentContains))
        {
            var epoch = await ListEpoch.GetFeedbacksListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.FeedbacksPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            await Cache.SetJsonAsync(cacheKey, result, ct);
        }

        return result;
    } // Kết thúc GetPagedAsync.

    // Tắt cache JSON danh sách CTE khi có lọc nặng (cùng tinh thần SuppressCommentRouteCache trong CommentAPI).
    private static bool SuppressFeedbackRouteCache(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        string? contentContains) =>
        HasCreatedAtFilter(createdAtFrom, createdAtTo)
        || userId is not null
        || HasTextFilter(contentContains);

    // GET .../feedbacks/cte — phân trang theo dòng phẳng CTE (mỗi item một nút + Level).
    public async Task<PagedResult<FeedbackCteDto>> GetCteFlatRoutePagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        string? contentContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default)
    { // Mở khối GetCteFlatRoutePagedAsync.
        // BƯỚC 1 — Chuẩn hóa sort + phân trang (trùng quy tắc CommentsController).
        var sortSpec = ListSortParsing.ParseFeedbackSort(sort, sortDir);
        var (p, s) = PaginationQuery.Normalize(page, pageSize);
        // BƯỚC 2 — Cache-aside khi không suppress (epoch chung InvalidateFeedbacksListsAsync).
        if (!SuppressFeedbackRouteCache(createdAtFrom, createdAtTo, userId, contentContains))
        { // Mở nhánh đọc cache.
            var epoch = await ListEpoch.GetFeedbacksListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.FeedbacksCteFlat(epoch, p, s, sortSpec.CacheSegment, sortSpec.Descending);
            var cached = await Cache.GetJsonAsync<PagedResult<FeedbackCteDto>>(cacheKey, ct);
            if (cached is not null)
                return cached;
        } // Kết thúc đọc cache.

        // BƯỚC 3 — Một round-trip CTE + sort RAM; cắt trang trên list phẳng.
        var flatRows = await _repository.LoadRawCteAsync(ct, createdAtFrom, createdAtTo, userId, contentContains, sortSpec);
        var totalRows = (long)flatRows.Count;
        var pageItems = flatRows
            .Skip((p - 1) * s)
            .Take(s)
            .ToList();
        var result = FeedbackPagedResult.ForFlatFeedbackCteList(pageItems, p, s, totalRows);

        // BƯỚC 4 — Ghi cache khi được phép.
        if (!SuppressFeedbackRouteCache(createdAtFrom, createdAtTo, userId, contentContains))
        { // Mở nhánh ghi cache.
            var epoch = await ListEpoch.GetFeedbacksListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.FeedbacksCteFlat(epoch, p, s, sortSpec.CacheSegment, sortSpec.Descending);
            await Cache.SetJsonAsync(cacheKey, result, ct);
        } // Kết thúc ghi cache.

        return result;
    } // Kết thúc GetCteFlatRoutePagedAsync.

    // GET .../feedbacks/tree/cte — phân trang theo gốc thread; mỗi item là cây lồng FeedbackTreeCteDto.
    public async Task<PagedResult<FeedbackTreeCteDto>> GetTreeCteRoutePagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        string? contentContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default)
    { // Mở khối GetTreeCteRoutePagedAsync.
        var sortSpec = ListSortParsing.ParseFeedbackSort(sort, sortDir);
        var (p, s) = PaginationQuery.Normalize(page, pageSize);
        if (!SuppressFeedbackRouteCache(createdAtFrom, createdAtTo, userId, contentContains))
        {
            var epoch = await ListEpoch.GetFeedbacksListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.FeedbacksTreeCte(epoch, p, s, sortSpec.CacheSegment, sortSpec.Descending);
            var cached = await Cache.GetJsonAsync<PagedResult<FeedbackTreeCteDto>>(cacheKey, ct);
            if (cached is not null)
                return cached;
        }

        var (items, totalNodes, totalFeedbacks) = await BuildFeedbackCteTreesPagedCoreAsync(
            p, s, ct, createdAtFrom, createdAtTo, userId, contentContains, sortSpec);
        var result = FeedbackPagedResult.ForCtePagedByRootNodes(items, p, s, totalFeedbacks, totalNodes);

        if (!SuppressFeedbackRouteCache(createdAtFrom, createdAtTo, userId, contentContains))
        {
            var epoch = await ListEpoch.GetFeedbacksListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.FeedbacksTreeCte(epoch, p, s, sortSpec.CacheSegment, sortSpec.Descending);
            await Cache.SetJsonAsync(cacheKey, result, ct);
        }

        return result;
    } // Kết thúc GetTreeCteRoutePagedAsync.

    // GET .../feedbacks/tree/cte/flatten — preorder các thread gốc của trang → danh sách phẳng có Level.
    public async Task<PagedResult<FeedbackFlattenCteDto>> GetTreeCteFlattenRoutePagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        string? contentContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default)
    { // Mở khối GetTreeCteFlattenRoutePagedAsync.
        var sortSpec = ListSortParsing.ParseFeedbackSort(sort, sortDir);
        var (p, s) = PaginationQuery.Normalize(page, pageSize);
        if (!SuppressFeedbackRouteCache(createdAtFrom, createdAtTo, userId, contentContains))
        {
            var epoch = await ListEpoch.GetFeedbacksListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.FeedbacksTreeCteFlatten(epoch, p, s, sortSpec.CacheSegment, sortSpec.Descending);
            var cached = await Cache.GetJsonAsync<PagedResult<FeedbackFlattenCteDto>>(cacheKey, ct);
            if (cached is not null)
                return cached;
        }

        var (cteRootsOnPage, totalCteRootNodes, totalFeedbacksMatchingFilter) = await BuildFeedbackCteTreesPagedCoreAsync(
            p, s, ct, createdAtFrom, createdAtTo, userId, contentContains, sortSpec);
        var preorderRows = new List<FeedbackFlattenCteDto>();
        foreach (var root in cteRootsOnPage)
            FlattenTreeCteToFlattenCteDto(root, preorderRows);
        var result = FeedbackPagedResult.ForCtePagedByRootNodes(
            preorderRows, p, s, totalFeedbacksMatchingFilter, totalCteRootNodes);

        if (!SuppressFeedbackRouteCache(createdAtFrom, createdAtTo, userId, contentContains))
        {
            var epoch = await ListEpoch.GetFeedbacksListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.FeedbacksTreeCteFlatten(epoch, p, s, sortSpec.CacheSegment, sortSpec.Descending);
            await Cache.SetJsonAsync(cacheKey, result, ct);
        }

        return result;
    } // Kết thúc GetTreeCteFlattenRoutePagedAsync.

    // Pipeline CTE: COUNT bảng + LoadRawCteAsync + BuildFeedbackTreeCte + sort gốc + Skip/Take theo thread (mirror BuildCteTreesPagedCoreAsync).
    private async Task<(List<FeedbackTreeCteDto> PagedRoots, long TotalRootNodesInCte, long TotalFeedbacksInTable)> BuildFeedbackCteTreesPagedCoreAsync(
        int page,
        int pageSize,
        CancellationToken ct,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        string? contentContains,
        FeedbackListSort sortSpec)
    { // Mở khối BuildFeedbackCteTreesPagedCoreAsync.
        // BƯỚC 1 — Tổng feedback khớp lọc trên bảng (TotalComments / metadata).
        var totalFeedbacksTable = await _repository.CountFeedbacksMatchingRouteAsync(
            createdAtFrom, createdAtTo, userId, contentContains, ct);
        // BƯỚC 2 — Toàn bộ hàng CTE đã sort phẳng.
        var rows = await _repository.LoadRawCteAsync(ct, createdAtFrom, createdAtTo, userId, contentContains, sortSpec);
        // BƯỚC 3 — Dựng rừng gốc (một “PostId” toàn hệ — không GroupBy).
        var roots = BuildFeedbackTreeCte(rows);
        // BƯỚC 4 — Sắp danh sách gốc rồi phân trang trong RAM.
        var orderedRoots = FeedbackRepository.SortFeedbackTreeCteRootsForPaging(roots, sortSpec);
        var totalRootNodesInCte = (long)orderedRoots.Count;
        var pagedRoots = orderedRoots
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        return (pagedRoots, totalRootNodesInCte, totalFeedbacksTable);
    } // Kết thúc BuildFeedbackCteTreesPagedCoreAsync.

    // Dựng FeedbackTreeCteDto từ hàng phẳng: lookup Id; orphan/cycle → nâng thành gốc (cùng CommentAPI BuildTreeCte nhưng không nhóm PostId).
    private static List<FeedbackTreeCteDto> BuildFeedbackTreeCte(List<FeedbackCteDto> rows)
    { // Mở khối BuildFeedbackTreeCte.
        if (rows is null || rows.Count == 0)
            return new List<FeedbackTreeCteDto>();

        // BƯỚC 0 — Map Id → ParentId một lần cho cả tập (CommentAPI gọi ToDictionary trong HasCycleCte mỗi nút → O(n²); ở đây một cây toàn hệ n lớn nên phải tránh).
        var parentById = rows.ToDictionary(x => x.Id, x => x.ParentId);

        var forest = new List<FeedbackTreeCteDto>();
        var lookup = rows.ToDictionary(
            x => x.Id,
            x => new FeedbackTreeCteDto
            {
                Id = x.Id,
                Content = x.Content,
                CreatedAt = x.CreatedAt,
                UserId = x.UserId,
                ParentId = x.ParentId,
                IsResolved = x.IsResolved,
                IsPinned = x.IsPinned,
                Level = x.Level,
            });

        var threadRoots = new List<FeedbackTreeCteDto>();
        foreach (var row in rows.OrderBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id))
        { // Duyệt theo Level để cha luôn xử lý trước con khi nối Children.
            var node = lookup[row.Id];
            if (row.ParentId is null)
            {
                threadRoots.Add(node);
                continue;
            }

            if (!lookup.TryGetValue(row.ParentId.Value, out var parent))
            {
                threadRoots.Add(node);
                continue;
            }

            if (HasCycleFeedbackCte(row.Id, parentById))
            {
                threadRoots.Add(node);
                continue;
            }

            parent.Children.Add(node);
        }

        foreach (var root in threadRoots.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id))
            forest.Add(root);

        return forest;
    } // Kết thúc BuildFeedbackTreeCte.

    // Leo ngược ParentId trong map đã có để phát hiện chu trình (không tạo lại Dictionary mỗi lần gọi).
    private static bool HasCycleFeedbackCte(Guid feedbackId, IReadOnlyDictionary<Guid, Guid?> parentById)
    { // Mở khối HasCycleFeedbackCte.
        if (!parentById.ContainsKey(feedbackId))
            return false;

        var visited = new HashSet<Guid>();
        Guid? parentId = parentById[feedbackId];
        while (parentId is not null)
        {
            if (parentId == feedbackId)
                return true;
            if (!visited.Add(parentId.Value))
                return true;
            if (!parentById.TryGetValue(parentId.Value, out var nextParent))
                return false;
            parentId = nextParent;
        }

        return false;
    } // Kết thúc HasCycleFeedbackCte.

    // Preorder một nhánh FeedbackTreeCteDto → danh sách FeedbackFlattenCteDto
    private static void FlattenTreeCteToFlattenCteDto(FeedbackTreeCteDto node, ICollection<FeedbackFlattenCteDto> sink)
    { // Mở khối FlattenTreeCteToFlattenCteDto.
        sink.Add(new FeedbackFlattenCteDto
        {
            Id = node.Id,
            Content = node.Content,
            CreatedAt = node.CreatedAt,
            UserId = node.UserId,
            ParentId = node.ParentId,
            IsResolved = node.IsResolved,
            IsPinned = node.IsPinned,
            Level = node.Level,
        });
        foreach (var child in node.Children.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id))
            FlattenTreeCteToFlattenCteDto(child, sink);
    } // Kết thúc FlattenTreeCteToFlattenCteDto.

    public async Task<FeedbackDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        var key = EntityCacheKeys.Feedback(id);
        var cached = await Cache.GetJsonAsync<FeedbackDto>(key, ct);
        if (cached is not null)
            return cached;

        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Feedback not found.");
        var dto = _mapper.Map<FeedbackDto>(entity);
        await Cache.SetJsonAsync(key, dto, ct);
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
        await Cache.SetJsonAsync(EntityCacheKeys.Feedback(entity.Id), dtoOut, ct);
        await ListEpoch.InvalidateFeedbacksListsAsync(ct);
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
        await Cache.RemoveAsync(EntityCacheKeys.Feedback(id), ct);
        await ListEpoch.InvalidateFeedbacksListsAsync(ct);
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
        await Cache.RemoveAsync(EntityCacheKeys.Feedback(id), ct);
        await ListEpoch.InvalidateFeedbacksListsAsync(ct);
    } // Kết thúc UpdateAsAdminAsync.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
        await Cache.RemoveAsync(EntityCacheKeys.Feedback(id), ct);
        await ListEpoch.InvalidateFeedbacksListsAsync(ct);
    } // Kết thúc SoftDeleteAsync.
}
