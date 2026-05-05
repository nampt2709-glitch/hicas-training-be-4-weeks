using AutoMapper;
using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Entities;
using CommentAPI.Interfaces;
using CommentAPI.Repositories;
using Microsoft.AspNetCore.Http;

namespace CommentAPI.Services;

// Nghiệp vụ Post: danh sách có cache khi không filter; CRUD + phân quyền tác giả vs admin.
public class PostService : ServiceBase, IPostService
{
    #region Trường & hàm tạo — PostsController

    private readonly IPostRepository _repository; // Truy vấn projection PostDto / entity tracked.
    private readonly IUserRepository _userRepository; // ExistsAsync cho FK UserId.
    private readonly IMapper _mapper; // Entity Post ↔ DTO tạo mới.
    private readonly ICacheListEpochStore _listEpoch; // Invalidate danh sách pst:* sau CRUD post / cascade comment.

    public PostService(
        IPostRepository repository,
        IUserRepository userRepository,
        IMapper mapper,
        IEntityResponseCache cache,
        ICacheListEpochStore listEpoch)
        : base(cache)
    {
        _repository = repository;
        _userRepository = userRepository;
        _mapper = mapper;
        _listEpoch = listEpoch;
    }

    #endregion

    #region Route Functions

    // [1] GET /api/posts — cache-aside khi không có filter ngày/title/content.
    public async Task<PagedResult<PostDto>> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        string? titleContains = null,
        string? contentContains = null,
        SortByColumn? sort = null)
    {
        var sortKey = sort ?? PostRepository.PostListSortDefault;
        // BƯỚC 1: Nếu không có filter list — thử đọc cache theo (page, pageSize) và epoch pst hiện tại.
        if (!HasPostListFilter(createdAtFrom, createdAtTo, titleContains, contentContains))
        {
            var pst = await _listEpoch.GetPostsListEpochAsync(cancellationToken);
            var cacheKey = EntityCacheKeys.PostsPaged(pst, page, pageSize, sortKey);
            var cached = await Cache.GetJsonAsync<PagedResult<PostDto>>(cacheKey, cancellationToken);
            if (cached is not null)
                return cached;
        }

        // BƯỚC 2: Miss hoặc có filter — gọi repository COUNT + SELECT trang.
        var (items, total) = await _repository.GetPagedAsync(
            page,
            pageSize,
            cancellationToken,
            createdAtFrom,
            createdAtTo,
            titleContains,
            contentContains,
            sort);

        // BƯỚC 3: Gói PagedResult thủ công (không dùng helper static của comment).
        var result = new PagedResult<PostDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };

        // BƯỚC 4: Ghi cache chỉ khi không filter — khóa gắn epoch để InvalidatePostsListAsync sau CRUD không trả snapshot cũ.
        if (!HasPostListFilter(createdAtFrom, createdAtTo, titleContains, contentContains))
        {
            var pst = await _listEpoch.GetPostsListEpochAsync(cancellationToken);
            await Cache.SetJsonAsync(EntityCacheKeys.PostsPaged(pst, page, pageSize, sortKey), result, cancellationToken);
        }

        return result;
    }

    // [2] GET /api/posts/{id} — cache-aside theo Id post.
    public async Task<PostDto> GetByIdAsync(Guid id)
    {
        var cacheKey = EntityCacheKeys.Post(id);
        var cached = await Cache.GetJsonAsync<PostDto>(cacheKey, default);
        if (cached is not null)
            return cached;

        var dto = await _repository.GetByIdForReadAsync(id, default);
        if (dto is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.PostNotFound,
                ApiMessages.PostNotFound);
        }

        await Cache.SetJsonAsync(cacheKey, dto, default);
        return dto;
    }

    // [3] POST /api/posts — kiểm tra UserId tồn tại rồi Insert.
    public async Task<PostDto> CreateAsync(CreatePostDto dto)
    {
        if (!await _userRepository.ExistsAsync(dto.UserId))
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.UserNotFound,
                ApiMessages.UserNotFound);
        }

        var entity = _mapper.Map<Post>(dto);
        entity.Id = Guid.NewGuid();

        await _repository.AddAsync(entity);
        await _repository.SaveChangesAsync();

        // Bơm epoch pst — danh sách GET /api/posts không filter phải miss cache (bài mới).
        await _listEpoch.InvalidatePostsListAsync(default);

        return _mapper.Map<PostDto>(entity);
    }

    // [4] PUT /api/posts/{id} — chỉ chủ bài (UserId == currentUserId) được sửa title/content.
    public async Task UpdateAsAuthorAsync(Guid id, UpdatePostDto dto, Guid currentUserId)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.PostNotFound,
                ApiMessages.PostNotFound);
        }

        if (entity.UserId != currentUserId)
        {
            throw new ApiException(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.NotResourceAuthor,
                ApiMessages.NotResourceAuthor);
        }

        entity.Title = dto.Title;
        entity.Content = dto.Content;
        _repository.Update(entity);
        await _repository.SaveChangesAsync();

        await Cache.RemoveAsync(EntityCacheKeys.Post(id), default);
        await _listEpoch.InvalidatePostsListAsync(default);
    }

    // [5] PUT /api/admin/posts/{id} — admin có thể đổi UserId nếu gửi dto.UserId và user đích tồn tại.
    public async Task UpdateAsAdminAsync(Guid id, AdminUpdatePostDto dto)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.PostNotFound,
                ApiMessages.PostNotFound);
        }

        if (dto.UserId is { } u)
        {
            if (!await _userRepository.ExistsAsync(u))
            {
                throw new ApiException(
                    StatusCodes.Status404NotFound,
                    ApiErrorCodes.UserNotFound,
                    ApiMessages.UserNotFound);
            }

            entity.UserId = u;
        }

        entity.Title = dto.Title;
        entity.Content = dto.Content;
        _repository.Update(entity);
        await _repository.SaveChangesAsync();

        await Cache.RemoveAsync(EntityCacheKeys.Post(id), default);
        await _listEpoch.InvalidatePostsListAsync(default);
    }

    // [6] DELETE /api/posts/{id} — xóa cache trước rồi Remove entity (cascade comment tùy model).
    public async Task DeleteAsync(Guid id)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.PostNotFound,
                ApiMessages.PostNotFound);
        }

        await Cache.RemoveAsync(EntityCacheKeys.Post(id), default);
        // Cascade xóa comment: vô hiệu cache riêng GET …/posts/{id}/comments/tree|flat (khóa l:posts:…:comments:*).
        await Cache.RemoveManyAsync(EntityCacheKeys.PostsResourceCommentsCteAllKeys(id), default);

        _repository.Remove(entity);
        await _repository.SaveChangesAsync();

        // Xóa post làm đổi danh sách post + mọi danh sách/route comment gắn bài viết đã cascade.
        await _listEpoch.InvalidatePostsListAsync(default);
        await _listEpoch.InvalidateCommentsListsAsync(default);
    }

    #endregion

    #region Helpers

    // true = có ít nhất một filter (ngày / title / content) → không dùng cache list mặc định.
    private static bool HasPostListFilter(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        string? titleContains,
        string? contentContains) =>
        HasCreatedAtFilter(createdAtFrom, createdAtTo)
        || !string.IsNullOrWhiteSpace(titleContains)
        || !string.IsNullOrWhiteSpace(contentContains);

    #endregion
}
