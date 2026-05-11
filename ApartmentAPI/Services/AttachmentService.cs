using ApartmentAPI.DTOs; // PagedResult và DTO phân trang.
using ApartmentAPI.V1.DTOs; // AttachmentDto, form upload theo từng scope.
using ApartmentAPI.Entities; // Entity Attachment, User, AttachmentScope.
using ApartmentAPI.Repositories; // IAttachmentRepository, IFeedbackRepository.
using AutoMapper; // Map entity ↔ DTO.
using Microsoft.AspNetCore.Http; // IFormFile.
using Microsoft.AspNetCore.Identity; // UserManager<User>.

namespace ApartmentAPI.Services;

// Nghiệp vụ file đính kèm: danh sách phân trang + cache; tạo/sửa qua route cố định scope (avatar vs feedback).
public interface IAttachmentService
{
    Task<PagedResult<AttachmentDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        Guid? feedbackId,
        AttachmentScope? scope,
        string? originalFileNameContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default);

    Task<AttachmentDto> GetByIdAsync(Guid id, CancellationToken ct = default);

    // POST .../attachments/users/{userId}/avatar — scope Avatar, UserId từ route.
    Task<AttachmentDto> CreateAvatarForUserAsync(Guid userId, IFormFile file, CancellationToken ct = default);

    // POST .../feedbacks/{feedbackId}/files — scope Feedback; UserId = tác giả feedback.
    Task<AttachmentDto> CreateForFeedbackAsync(Guid feedbackId, IFormFile file, CancellationToken ct = default);

    // PUT .../attachments/{id}/avatar — chỉ thay file; giữ scope Avatar và UserId hiện có.
    Task UpdateAvatarAsync(Guid id, IFormFile? file, CancellationToken ct = default);

    // PUT .../attachments/{id}/feedback — đổi file (tuỳ chọn) và FK feedback + đồng bộ UserId theo tác giả feedback đích.
    Task UpdateFeedbackAttachmentAsync(Guid id, Guid feedbackId, IFormFile? file, CancellationToken ct = default);

    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

// CRUD Attachment — không còn một POST chung với field Scope; server gán Scope/FK theo route.
public sealed class AttachmentService : ServiceBase, IAttachmentService
{
    private readonly IAttachmentRepository _repository; // Truy vấn/ghi Attachment.
    private readonly UserManager<User> _users; // Kiểm tra UserId (avatar).
    private readonly IFeedbackRepository _feedbacks; // Nạp feedback + Exists.
    private readonly IMapper _mapper; // Ánh xạ entity ↔ DTO.
    private readonly IAttachmentFileStorage _fileStorage; // Lưu/xóa file vật lý.

    public AttachmentService(
        IAttachmentRepository repository,
        UserManager<User> users,
        IFeedbackRepository feedbacks,
        IMapper mapper,
        IEntityResponseCache cache,
        ICacheListEpochStore listEpoch,
        IAttachmentFileStorage fileStorage)
        : base(cache, listEpoch)
    { // Mở khối constructor.
        _repository = repository;
        _users = users;
        _feedbacks = feedbacks;
        _mapper = mapper;
        _fileStorage = fileStorage;
    } // Kết thúc constructor.

    private static bool HasListFilter(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        Guid? feedbackId,
        AttachmentScope? scope,
        string? originalFileNameContains) =>
        HasCreatedAtFilter(createdAtFrom, createdAtTo)
        || userId.HasValue
        || feedbackId.HasValue
        || scope.HasValue
        || HasTextFilter(originalFileNameContains);

    public async Task<PagedResult<AttachmentDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        Guid? feedbackId,
        AttachmentScope? scope,
        string? originalFileNameContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        var sortSpec = ListSortParsing.ParseAttachmentSort(sort, sortDir);

        if (!HasListFilter(createdAtFrom, createdAtTo, userId, feedbackId, scope, originalFileNameContains))
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
            scope,
            originalFileNameContains,
            sortSpec,
            ct);

        var dtos = _mapper.Map<List<AttachmentDto>>(items);
        var result = new PagedResult<AttachmentDto> { Items = dtos, Page = p, PageSize = s, TotalCount = total };

        if (!HasListFilter(createdAtFrom, createdAtTo, userId, feedbackId, scope, originalFileNameContains))
        {
            var epoch = await ListEpoch.GetAttachmentsListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.AttachmentsPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            await Cache.SetJsonAsync(cacheKey, result, ct);
        }

        return result;
    } // Kết thúc GetPagedAsync.

    public async Task<AttachmentDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
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
    } // Kết thúc GetByIdAsync.

    public async Task<AttachmentDto> CreateAvatarForUserAsync(Guid userId, IFormFile file, CancellationToken ct = default)
    { // Mở khối CreateAvatarForUserAsync.
        // BƯỚC 1 — User trong route phải tồn tại.
        if (await _users.FindByIdAsync(userId.ToString()) is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");
        // BƯỚC 2 — Lưu file vật lý + metadata từ luồng upload.
        var saved = await _fileStorage.SaveNewAsync(file, ct);
        // BƯỚC 3 — Entity: scope Avatar, không gắn feedback.
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
        };
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        var dtoOut = _mapper.Map<AttachmentDto>(entity);
        await Cache.SetJsonAsync(EntityCacheKeys.Attachment(entity.Id), dtoOut, ct);
        await ListEpoch.InvalidateAttachmentsListsAsync(ct);
        return dtoOut;
    } // Kết thúc CreateAvatarForUserAsync.

    public async Task<AttachmentDto> CreateForFeedbackAsync(Guid feedbackId, IFormFile file, CancellationToken ct = default)
    { // Mở khối CreateForFeedbackAsync.
        // BƯỚC 1 — Nạp feedback để lấy tác giả (UserId) và chắc FK tồn tại.
        var fb = await _feedbacks.GetByIdAsync(feedbackId, ct);
        if (fb is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Feedback not found.");
        // BƯỚC 2 — Lưu file.
        var saved = await _fileStorage.SaveNewAsync(file, ct);
        // BƯỚC 3 — Scope Feedback; UserId đồng bộ từ feedback — client không gửi UserId (tránh sai tác giả).
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
        };
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        var dtoOut = _mapper.Map<AttachmentDto>(entity);
        await Cache.SetJsonAsync(EntityCacheKeys.Attachment(entity.Id), dtoOut, ct);
        await ListEpoch.InvalidateAttachmentsListsAsync(ct);
        return dtoOut;
    } // Kết thúc CreateForFeedbackAsync.

    public async Task UpdateAvatarAsync(Guid id, IFormFile? file, CancellationToken ct = default)
    { // Mở khối UpdateAvatarAsync.
        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Attachment not found.");
        if (tracked.Scope != AttachmentScope.Avatar)
            throw ApiException.BadRequest(ApiErrorCodes.Validation, "This attachment is not an avatar; use the feedback-scoped update route.");

        if (file != null)
        { // Thay nội dung file.
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
    } // Kết thúc UpdateAvatarAsync.

    public async Task UpdateFeedbackAttachmentAsync(Guid id, Guid feedbackId, IFormFile? file, CancellationToken ct = default)
    { // Mở khối UpdateFeedbackAttachmentAsync.
        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Attachment not found.");
        if (tracked.Scope != AttachmentScope.Feedback)
            throw ApiException.BadRequest(ApiErrorCodes.Validation, "This attachment is not feedback-scoped; use the avatar update route.");

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
        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
        await Cache.RemoveAsync(EntityCacheKeys.Attachment(id), ct);
        await ListEpoch.InvalidateAttachmentsListsAsync(ct);
    } // Kết thúc UpdateFeedbackAttachmentAsync.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
        await Cache.RemoveAsync(EntityCacheKeys.Attachment(id), ct);
        await ListEpoch.InvalidateAttachmentsListsAsync(ct);
    } // Kết thúc SoftDeleteAsync.
}
