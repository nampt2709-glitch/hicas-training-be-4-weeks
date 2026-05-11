using ApartmentAPI.DTOs; // PagedResult và DTO phân trang.
using ApartmentAPI.V1.DTOs; // AttachmentDto, form upload theo từng scope.
using ApartmentAPI.Entities; // Entity Attachment, User, AttachmentScope.
using ApartmentAPI.Repositories; // IAttachmentRepository, IFeedbackRepository, IPostRepository.
using AutoMapper; // Map entity ↔ DTO.
using Microsoft.AspNetCore.Http; // IFormFile.
using Microsoft.AspNetCore.Identity; // UserManager<User>.

namespace ApartmentAPI.Services;

// Nghiệp vụ file đính kèm: danh sách phân trang + cache; tạo/sửa qua route cố định scope (avatar / feedback / bài đăng).
public interface IAttachmentService
{
    Task<PagedResult<AttachmentDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        Guid? feedbackId,
        Guid? postId,
        AttachmentScope? scope,
        string? originalFileNameContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default);

    Task<AttachmentDto> GetByIdAsync(Guid id, CancellationToken ct = default);

    // POST .../attachments/users/{userId}/avatar — scope Avatar, UserId từ route.
    Task<AttachmentDto> CreateAvatarForUserAsync(Guid userId, IFormFile file, CancellationToken ct = default);

    // POST .../uploads/avatar — thay mọi đính kèm Avatar cũ của user + cập nhật User.AvatarUrl (file mới).
    Task<(AttachmentDto Data, bool ReplacedExisting)> CreateOrReplaceAvatarForUserAsync(
        Guid userId,
        IFormFile file,
        string? replacedBy,
        CancellationToken ct = default);

    // POST .../feedbacks/{feedbackId}/files — scope Feedback; UserId = tác giả feedback.
    Task<AttachmentDto> CreateForFeedbackAsync(Guid feedbackId, IFormFile file, CancellationToken ct = default);

    // POST .../posts/{postId}/files — scope Post; UserId = tác giả bài đăng.
    Task<AttachmentDto> CreateForPostAsync(Guid postId, IFormFile file, CancellationToken ct = default);

    // PUT .../attachments/{id}/avatar — chỉ thay file; giữ scope Avatar và UserId hiện có.
    Task UpdateAvatarAsync(Guid id, IFormFile? file, CancellationToken ct = default);

    // PUT .../attachments/{id}/feedback — đổi file (tuỳ chọn) và FK feedback + đồng bộ UserId theo tác giả feedback đích.
    Task UpdateFeedbackAttachmentAsync(Guid id, Guid feedbackId, IFormFile? file, CancellationToken ct = default);

    // PUT .../attachments/{id}/post — đổi file (tuỳ chọn) và FK post + đồng bộ UserId theo tác giả bài đăng.
    Task UpdatePostAttachmentAsync(Guid id, Guid postId, IFormFile? file, CancellationToken ct = default);

    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

// CRUD Attachment — server gán Scope/FK theo route chứ không nhận enum từ client một cửa.
public sealed class AttachmentService : ServiceBase, IAttachmentService
{
    private readonly IAttachmentRepository _repository;
    private readonly UserManager<User> _users;
    private readonly IFeedbackRepository _feedbacks;
    private readonly IPostRepository _posts;
    private readonly IMapper _mapper;
    private readonly IAttachmentFileStorage _fileStorage;

    public AttachmentService(
        IAttachmentRepository repository,
        UserManager<User> users,
        IFeedbackRepository feedbacks,
        IPostRepository posts,
        IMapper mapper,
        IEntityResponseCache cache,
        ICacheListEpochStore listEpoch,
        IAttachmentFileStorage fileStorage)
        : base(cache, listEpoch)
    {
        _repository = repository;
        _users = users;
        _feedbacks = feedbacks;
        _posts = posts;
        _mapper = mapper;
        _fileStorage = fileStorage;
    }

    private static bool HasListFilter(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        Guid? feedbackId,
        Guid? postId,
        AttachmentScope? scope,
        string? originalFileNameContains) =>
        HasCreatedAtFilter(createdAtFrom, createdAtTo)
        || userId.HasValue
        || feedbackId.HasValue
        || postId.HasValue
        || scope.HasValue
        || HasTextFilter(originalFileNameContains);

    public async Task<PagedResult<AttachmentDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        Guid? feedbackId,
        Guid? postId,
        AttachmentScope? scope,
        string? originalFileNameContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default)
    {
        var sortSpec = ListSortParsing.ParseAttachmentSort(sort, sortDir);

        if (!HasListFilter(createdAtFrom, createdAtTo, userId, feedbackId, postId, scope, originalFileNameContains))
        {
            var epoch = await ListEpoch.GetAttachmentsListEpochAsync(ct);
            var key = EntityCacheKeys.AttachmentsPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            var cached = await Cache.GetJsonAsync<PagedResult<AttachmentDto>>(key, ct);
            if (cached is not null)
                return cached;
        }

        var (items, total, p, s) = await _repository.GetPagedAsync(
            page,
            pageSize,
            createdAtFrom,
            createdAtTo,
            userId,
            feedbackId,
            postId,
            scope,
            originalFileNameContains,
            sortSpec,
            ct);

        var dtos = _mapper.Map<List<AttachmentDto>>(items);
        var result = new PagedResult<AttachmentDto> { Items = dtos, Page = p, PageSize = s, TotalCount = total };

        if (!HasListFilter(createdAtFrom, createdAtTo, userId, feedbackId, postId, scope, originalFileNameContains))
        {
            var epoch = await ListEpoch.GetAttachmentsListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.AttachmentsPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            await Cache.SetJsonAsync(cacheKey, result, ct);
        }

        return result;
    }

    public async Task<AttachmentDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var key = EntityCacheKeys.Attachment(id);
        var cached = await Cache.GetJsonAsync<AttachmentDto>(key, ct);
        if (cached is not null)
            return cached;

        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Attachment not found.");
        var dto = _mapper.Map<AttachmentDto>(entity);
        await Cache.SetJsonAsync(key, dto, ct);
        return dto;
    }

    public async Task<AttachmentDto> CreateAvatarForUserAsync(Guid userId, IFormFile file, CancellationToken ct = default)
    {
        if (await _users.FindByIdAsync(userId.ToString()) is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");
        var saved = await _fileStorage.SaveNewAsync(file, ct);
        var entity = new Attachment
        {
            Scope = AttachmentScope.Avatar,
            OriginalFileName = saved.OriginalFileName,
            StoredFileName = saved.StoredFileName,
            FilePath = saved.RelativePath,
            ContentType = saved.ContentType,
            FileSize = saved.FileSize,
            FileHash = saved.Sha256Hex,
            UserId = userId,
            FeedbackId = null,
            PostId = null,
        };
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        var dtoOut = _mapper.Map<AttachmentDto>(entity);
        await Cache.SetJsonAsync(EntityCacheKeys.Attachment(entity.Id), dtoOut, ct);
        await ListEpoch.InvalidateAttachmentsListsAsync(ct);
        return dtoOut;
    }

    public async Task<(AttachmentDto Data, bool ReplacedExisting)> CreateOrReplaceAvatarForUserAsync(
        Guid userId,
        IFormFile file,
        string? replacedBy,
        CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");

        var existing = await _repository.GetActiveAvatarIdAndPathsForUserAsync(userId, ct);
        var hadExisting = existing.Count > 0;
        foreach (var (id, filePath) in existing)
        {
            _fileStorage.TryDeleteRelativeToContentRoot(filePath);
            await _repository.SoftDeleteAsync(id, replacedBy, ct);
            await Cache.RemoveAsync(EntityCacheKeys.Attachment(id), ct);
        }

        if (hadExisting)
            await _repository.SaveChangesAsync(ct);

        var saved = await _fileStorage.SaveNewAsync(file, ct);
        var entity = new Attachment
        {
            Scope = AttachmentScope.Avatar,
            OriginalFileName = saved.OriginalFileName,
            StoredFileName = saved.StoredFileName,
            FilePath = saved.RelativePath,
            ContentType = saved.ContentType,
            FileSize = saved.FileSize,
            FileHash = saved.Sha256Hex,
            UserId = userId,
            FeedbackId = null,
            PostId = null,
        };
        await _repository.AddAsync(entity, ct);

        user.AvatarUrl = saved.RelativePath;
        var identityUpdate = await _users.UpdateAsync(user);
        if (!identityUpdate.Succeeded)
        {
            throw ApiException.BadRequest(
                ApiErrorCodes.DatabaseUpdateFailed,
                string.Join("; ", identityUpdate.Errors.Select(e => e.Description)));
        }

        await _repository.SaveChangesAsync(ct);
        var dtoOut = _mapper.Map<AttachmentDto>(entity);
        await Cache.SetJsonAsync(EntityCacheKeys.Attachment(entity.Id), dtoOut, ct);
        await ListEpoch.InvalidateAttachmentsListsAsync(ct);
        return (dtoOut, hadExisting);
    }

    public async Task<AttachmentDto> CreateForFeedbackAsync(Guid feedbackId, IFormFile file, CancellationToken ct = default)
    {
        var fb = await _feedbacks.GetByIdAsync(feedbackId, ct);
        if (fb is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Feedback not found.");
        var saved = await _fileStorage.SaveNewAsync(file, ct);
        var entity = new Attachment
        {
            Scope = AttachmentScope.Feedback,
            OriginalFileName = saved.OriginalFileName,
            StoredFileName = saved.StoredFileName,
            FilePath = saved.RelativePath,
            ContentType = saved.ContentType,
            FileSize = saved.FileSize,
            FileHash = saved.Sha256Hex,
            UserId = fb.UserId,
            FeedbackId = feedbackId,
            PostId = null,
        };
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        var dtoOut = _mapper.Map<AttachmentDto>(entity);
        await Cache.SetJsonAsync(EntityCacheKeys.Attachment(entity.Id), dtoOut, ct);
        await ListEpoch.InvalidateAttachmentsListsAsync(ct);
        return dtoOut;
    }

    public async Task<AttachmentDto> CreateForPostAsync(Guid postId, IFormFile file, CancellationToken ct = default)
    {
        var post = await _posts.GetByIdAsync(postId, ct);
        if (post is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Post not found.");
        var saved = await _fileStorage.SaveNewAsync(file, ct);
        var entity = new Attachment
        {
            Scope = AttachmentScope.Post,
            OriginalFileName = saved.OriginalFileName,
            StoredFileName = saved.StoredFileName,
            FilePath = saved.RelativePath,
            ContentType = saved.ContentType,
            FileSize = saved.FileSize,
            FileHash = saved.Sha256Hex,
            UserId = post.UserId,
            FeedbackId = null,
            PostId = postId,
        };
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        var dtoOut = _mapper.Map<AttachmentDto>(entity);
        await Cache.SetJsonAsync(EntityCacheKeys.Attachment(entity.Id), dtoOut, ct);
        await ListEpoch.InvalidateAttachmentsListsAsync(ct);
        return dtoOut;
    }

    public async Task UpdateAvatarAsync(Guid id, IFormFile? file, CancellationToken ct = default)
    {
        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Attachment not found.");
        if (tracked.Scope != AttachmentScope.Avatar)
            throw ApiException.BadRequest(ApiErrorCodes.Validation, "This attachment is not an avatar; use the feedback- or post-scoped update route.");

        if (file != null)
        {
            _fileStorage.TryDeleteRelativeToContentRoot(tracked.FilePath);
            var saved = await _fileStorage.SaveNewAsync(file, ct);
            tracked.OriginalFileName = saved.OriginalFileName;
            tracked.StoredFileName = saved.StoredFileName;
            tracked.FilePath = saved.RelativePath;
            tracked.ContentType = saved.ContentType;
            tracked.FileSize = saved.FileSize;
            tracked.FileHash = saved.Sha256Hex;
        }

        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
        await Cache.RemoveAsync(EntityCacheKeys.Attachment(id), ct);
        await ListEpoch.InvalidateAttachmentsListsAsync(ct);
    }

    public async Task UpdateFeedbackAttachmentAsync(Guid id, Guid feedbackId, IFormFile? file, CancellationToken ct = default)
    {
        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Attachment not found.");
        if (tracked.Scope != AttachmentScope.Feedback)
            throw ApiException.BadRequest(ApiErrorCodes.Validation, "This attachment is not feedback-scoped; use the avatar or post update route.");

        var fb = await _feedbacks.GetByIdAsync(feedbackId, ct);
        if (fb is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Feedback not found.");

        if (file != null)
        {
            _fileStorage.TryDeleteRelativeToContentRoot(tracked.FilePath);
            var saved = await _fileStorage.SaveNewAsync(file, ct);
            tracked.OriginalFileName = saved.OriginalFileName;
            tracked.StoredFileName = saved.StoredFileName;
            tracked.FilePath = saved.RelativePath;
            tracked.ContentType = saved.ContentType;
            tracked.FileSize = saved.FileSize;
            tracked.FileHash = saved.Sha256Hex;
        }

        tracked.FeedbackId = feedbackId;
        tracked.UserId = fb.UserId;
        tracked.PostId = null;
        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
        await Cache.RemoveAsync(EntityCacheKeys.Attachment(id), ct);
        await ListEpoch.InvalidateAttachmentsListsAsync(ct);
    }

    public async Task UpdatePostAttachmentAsync(Guid id, Guid postId, IFormFile? file, CancellationToken ct = default)
    {
        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Attachment not found.");
        if (tracked.Scope != AttachmentScope.Post)
            throw ApiException.BadRequest(ApiErrorCodes.Validation, "This attachment is not post-scoped; use the avatar or feedback update route.");

        var post = await _posts.GetByIdAsync(postId, ct);
        if (post is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Post not found.");

        if (file != null)
        {
            _fileStorage.TryDeleteRelativeToContentRoot(tracked.FilePath);
            var saved = await _fileStorage.SaveNewAsync(file, ct);
            tracked.OriginalFileName = saved.OriginalFileName;
            tracked.StoredFileName = saved.StoredFileName;
            tracked.FilePath = saved.RelativePath;
            tracked.ContentType = saved.ContentType;
            tracked.FileSize = saved.FileSize;
            tracked.FileHash = saved.Sha256Hex;
        }

        tracked.PostId = postId;
        tracked.UserId = post.UserId;
        tracked.FeedbackId = null;
        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
        await Cache.RemoveAsync(EntityCacheKeys.Attachment(id), ct);
        await ListEpoch.InvalidateAttachmentsListsAsync(ct);
    }

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    {
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
        await Cache.RemoveAsync(EntityCacheKeys.Attachment(id), ct);
        await ListEpoch.InvalidateAttachmentsListsAsync(ct);
    }
}
