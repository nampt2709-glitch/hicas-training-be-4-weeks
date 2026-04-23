using AutoMapper; 
using CommentAPI; 
using CommentAPI.DTOs; 
using CommentAPI.Entities; 
using CommentAPI.Interfaces;
using Microsoft.AspNetCore.Http; 

namespace CommentAPI.Services; 

public class PostService : IPostService // CRUD + search + author/admin update paths.
{
    #region Trường & hàm tạo — PostsController

    private readonly IPostRepository _repository; // Post persistence.
    private readonly IUserRepository _userRepository; // Kiểm tra FK UserId.
    private readonly IMapper _mapper; // AutoMapper.
    private readonly IEntityResponseCache _cache; // Response cache.

    public PostService( // DI ctor.
        IPostRepository repository, // Post repo.
        IUserRepository userRepository, // User existence.
        IMapper mapper, // Mapper.
        IEntityResponseCache cache) // Cache.
    {
        _repository = repository; // Field.
        _userRepository = userRepository; // Field.
        _mapper = mapper; // Field.
        _cache = cache; // Field.
    }

    #endregion

    #region GET — PostsController (GetAll, GetById)

    public async Task<PagedResult<PostDto>> GetPagedAsync( // List posts paged.
        int page, // Page index.
        int pageSize, // Page size.
        CancellationToken cancellationToken = default, // CT.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null, // Lọc CreatedAt.
        string? titleContains = null, // Filter Title (Contains).
        string? contentContains = null) // Filter Content (Contains).
    {
        if (!HasPostListFilter(createdAtFrom, createdAtTo, titleContains, contentContains)) // Chỉ cache danh sách “thuần”.
        {
            var cacheKey = EntityCacheKeys.PostsPaged(page, pageSize); // Cache key.
            var cached = await _cache.GetJsonAsync<PagedResult<PostDto>>(cacheKey, cancellationToken); // Try cache.
            if (cached is not null) // Hit.
                return cached; // Fast path.
        }

        var (items, total) = await _repository.GetPagedAsync(
            page,
            pageSize,
            cancellationToken,
            createdAtFrom,
            createdAtTo,
            titleContains,
            contentContains); // DB projection.
        var result = new PagedResult<PostDto> // Wrap.
        {
            Items = items, // Page rows.
            Page = page, // Page.
            PageSize = pageSize, // Size.
            TotalCount = total // Count.
        };
        if (!HasPostListFilter(createdAtFrom, createdAtTo, titleContains, contentContains))
            await _cache.SetJsonAsync(EntityCacheKeys.PostsPaged(page, pageSize), result, cancellationToken); // Store.
        return result; // Out.
    }

    public async Task<PostDto> GetByIdAsync(Guid id) // Single post read.
    {
        var cacheKey = EntityCacheKeys.Post(id); // Key by id.
        var cached = await _cache.GetJsonAsync<PostDto>(cacheKey, default); // Read cache.
        if (cached is not null) // Hit.
        {
            return cached; // DTO.
        }

        var dto = await _repository.GetByIdForReadAsync(id, default); // No-tracking projection.
        if (dto is null) // Not found.
        {
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // 404.
                ApiErrorCodes.PostNotFound, // Code.
                ApiMessages.PostNotFound); // Msg.
        }

        await _cache.SetJsonAsync(cacheKey, dto, default); // Populate cache.
        return dto; // DTO.
    }

    #endregion

    #region POST — PostsController (Create)

    public async Task<PostDto> CreateAsync(CreatePostDto dto) // Insert post.
    {
        if (!await _userRepository.ExistsAsync(dto.UserId)) // FK guard.
        {
            throw new ApiException( // User missing.
                StatusCodes.Status404NotFound, // 404.
                ApiErrorCodes.UserNotFound, // Code.
                ApiMessages.UserNotFound); // Msg.
        }

        var entity = _mapper.Map<Post>(dto); // Map scalar fields.
        entity.Id = Guid.NewGuid(); // New PK.

        await _repository.AddAsync(entity); // Stage insert.
        await _repository.SaveChangesAsync(); // Commit.

        return _mapper.Map<PostDto>(entity); // Return mapped DTO (client nhận id mới).
    }

    #endregion

    #region PUT — PostsController (Update, UpdateAsAdmin)

    // User: chỉ chủ bài (UserId trùng JWT) được cập nhật tiêu đề/nội dung.
    public async Task UpdateAsAuthorAsync(Guid id, UpdatePostDto dto, Guid currentUserId) // Author path.
    {
        var entity = await _repository.GetByIdAsync(id); // Load for update.
        if (entity is null) // Missing post.
        {
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // 404.
                ApiErrorCodes.PostNotFound, // Code.
                ApiMessages.PostNotFound); // Msg.
        }

        if (entity.UserId != currentUserId) // Not owner.
        {
            throw new ApiException( // 403 business rule.
                StatusCodes.Status403Forbidden, // Forbidden.
                ApiErrorCodes.NotResourceAuthor, // Code.
                ApiMessages.NotResourceAuthor); // Msg.
        }

        entity.Title = dto.Title; // Apply title.
        entity.Content = dto.Content; // Apply body.
        _repository.Update(entity); // Mark modified.
        await _repository.SaveChangesAsync(); // Persist.

        await _cache.RemoveAsync(EntityCacheKeys.Post(id), default); // Invalidate read model.
    }

    // Admin: cập nhật tiêu đề/nội dung, tùy chọn đổi UserId nếu gửi giá trị.
    public async Task UpdateAsAdminAsync(Guid id, AdminUpdatePostDto dto) // Admin path.
    {
        var entity = await _repository.GetByIdAsync(id); // Load.
        if (entity is null) // Not found.
        {
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // 404.
                ApiErrorCodes.PostNotFound, // Code.
                ApiMessages.PostNotFound); // Msg.
        }

        if (dto.UserId is { } u) // Nullable pattern: có gửi UserId mới.
        {
            if (!await _userRepository.ExistsAsync(u)) // Target user must exist.
            {
                throw new ApiException( // 404.
                    StatusCodes.Status404NotFound, // 404.
                    ApiErrorCodes.UserNotFound, // Code.
                    ApiMessages.UserNotFound); // Msg.
            }

            entity.UserId = u; // Reassign owner.
        }

        entity.Title = dto.Title; // Title.
        entity.Content = dto.Content; // Content.
        _repository.Update(entity); // Modified.
        await _repository.SaveChangesAsync(); // Save.

        await _cache.RemoveAsync(EntityCacheKeys.Post(id), default); // Invalidate cache entry.
    }

    #endregion

    #region DELETE — PostsController (Delete)

    public async Task DeleteAsync(Guid id) // Remove post.
    {
        var entity = await _repository.GetByIdAsync(id); // Find tracked entity.
        if (entity is null) // Missing.
        {
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // 404.
                ApiErrorCodes.PostNotFound, // Code.
                ApiMessages.PostNotFound); // Msg.
        }

        await _cache.RemoveAsync(EntityCacheKeys.Post(id), default); // Drop cache before delete.

        _repository.Remove(entity); // Stage delete.
        await _repository.SaveChangesAsync(); // Commit cascade rules theo model.
    }

    #endregion

    #region Private helpers

    // Có bất kỳ filter list nào → không cache (tránh khóa sai hoặc bùng nổ).
    private static bool HasPostListFilter(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        string? titleContains,
        string? contentContains) =>
        createdAtFrom.HasValue
        || createdAtTo.HasValue
        || !string.IsNullOrWhiteSpace(titleContains)
        || !string.IsNullOrWhiteSpace(contentContains);

    #endregion
}
