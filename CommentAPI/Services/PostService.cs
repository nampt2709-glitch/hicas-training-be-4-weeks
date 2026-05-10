using AutoMapper; // Map Post entity ↔ PostDto / CreatePostDto.
using CommentAPI; // ApiException, ApiErrorCodes, ApiMessages.
using CommentAPI.DTOs; // PostDto, CreatePostDto, UpdatePostDto, AdminUpdatePostDto.
using CommentAPI.Entities; // Thực thể Post.
using CommentAPI.Interfaces; // IPostService, IPostRepository, IUserRepository, cache.
using CommentAPI.Repositories; // PostRepository.PostListSortDefault.
using Microsoft.AspNetCore.Http; // StatusCodes cho ApiException.

namespace CommentAPI.Services;

// =============================================================================
// File PostService.cs: nghiệp vụ Post — GET phân trang/chi tiết có cache; CRUD; bump epoch pst sau thay đổi danh sách.
// =============================================================================

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
    { // Mở khối constructor PostService.
        // BƯỚC 1 — Gán repository post (đọc/ghi bảng Posts + projection).
        _repository = repository; // Lưu IPostRepository đã đăng ký DI.
        // BƯỚC 2 — Gán repository user để kiểm tra FK trước Create/AdminUpdate.
        _userRepository = userRepository; // ExistsAsync tránh post mồ côi UserId.
        // BƯỚC 3 — Gán AutoMapper cho Map sau SaveChanges.
        _mapper = mapper; // Profile trong MappingProfile.
        // BƯỚC 4 — Gán store epoch danh sách post (prefix pst: trong EntityCacheKeys).
        _listEpoch = listEpoch; // Bump sau mỗi thay đổi ảnh hưởng GET /api/posts không filter.
    } // Kết thúc constructor PostService.

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
    { // Mở khối GetPagedAsync.
        var sortKey = sort ?? PostRepository.PostListSortDefault; // Sort mặc định repository (cột + hướng).

        // BƯỚC 1: Nếu không có filter list — thử đọc cache theo (page, pageSize) và epoch pst hiện tại.
        if (!HasPostListFilter(createdAtFrom, createdAtTo, titleContains, contentContains))
        {
            var pst = await _listEpoch.GetPostsListEpochAsync(cancellationToken); // Đọc epoch list post.
            var cacheKey = EntityCacheKeys.PostsPaged(pst, page, pageSize, sortKey); // Khóa JSON danh sách một trang.
            var cached = await Cache.GetJsonAsync<PagedResult<PostDto>>(cacheKey, cancellationToken);
            if (cached is not null) // Hit cache.
                return cached; // Trả ngay, không gọi DB.
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
            Items = items, // Một trang projection.
            Page = page, // Trang hiện tại (1-based).
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total, // Tổng bản ghi khớp lọc.
        };

        // BƯỚC 4: Ghi cache chỉ khi không filter — khóa gắn epoch để InvalidatePostsListAsync sau CRUD không trả snapshot cũ.
        if (!HasPostListFilter(createdAtFrom, createdAtTo, titleContains, contentContains))
        {
            var pst = await _listEpoch.GetPostsListEpochAsync(cancellationToken); // Đồng bộ epoch với lúc đọc (tránh lệch khi bump xen giữa).
            await Cache.SetJsonAsync(EntityCacheKeys.PostsPaged(pst, page, pageSize, sortKey), result, cancellationToken);
        }

        return result;
    } // Kết thúc GetPagedAsync.

    // [2] GET /api/posts/{id} — cache-aside theo Id post.
    public async Task<PostDto> GetByIdAsync(Guid id)
    { // Mở khối GetByIdAsync.
        // BƯỚC 1 — Tính khóa chi tiết post và thử cache.
        var cacheKey = EntityCacheKeys.Post(id); // Khóa p:{id:N}.
        var cached = await Cache.GetJsonAsync<PostDto>(cacheKey, default);
        if (cached is not null) // Hit.
            return cached; // Không SELECT DB.

        // BƯỚC 2 — Projection một dòng; null → 404 thống nhất.
        var dto = await _repository.GetByIdForReadAsync(id, default);
        if (dto is null) // Không tồn tại.
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.PostNotFound,
                ApiMessages.PostNotFound);
        }

        // BƯỚC 3 — Ghi cache rồi trả DTO.
        await Cache.SetJsonAsync(cacheKey, dto, default);
        return dto;
    } // Kết thúc GetByIdAsync.

    // [3] POST /api/posts — kiểm tra UserId tồn tại rồi Insert.
    public async Task<PostDto> CreateAsync(CreatePostDto dto)
    { // Mở khối CreateAsync.
        // BƯỚC 1 — Xác nhận user chủ bài tồn tại (tránh FK hoặc nghiệp vụ sai).
        if (!await _userRepository.ExistsAsync(dto.UserId))
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.UserNotFound,
                ApiMessages.UserNotFound);
        }

        // BƯỚC 2 — Map DTO → entity, gán Id mới, Add + SaveChanges.
        var entity = _mapper.Map<Post>(dto);
        entity.Id = Guid.NewGuid(); // Khóa do server cấp.

        await _repository.AddAsync(entity);
        await _repository.SaveChangesAsync();

        // BƯỚC 3 — Bơm epoch pst — danh sách GET /api/posts không filter phải miss cache (bài mới).
        await _listEpoch.InvalidatePostsListAsync(default);

        return _mapper.Map<PostDto>(entity); // Trả DTO sau khi đã persist.
    } // Kết thúc CreateAsync.

    // [4] PUT /api/posts/{id} — chỉ chủ bài (UserId == currentUserId) được sửa title/content.
    public async Task UpdateAsAuthorAsync(Guid id, UpdatePostDto dto, Guid currentUserId)
    { // Mở khối UpdateAsAuthorAsync.
        // BƯỚC 1 — Tải entity tracked; null → 404.
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.PostNotFound,
                ApiMessages.PostNotFound);
        }

        // BƯỚC 2 — So khớp chủ bài với currentUserId; sai → 403.
        if (entity.UserId != currentUserId)
        {
            throw new ApiException(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.NotResourceAuthor,
                ApiMessages.NotResourceAuthor);
        }

        // BƯỚC 3 — Áp Title/Content, Update + Save; xóa cache chi tiết + bump epoch list.
        entity.Title = dto.Title;
        entity.Content = dto.Content;
        _repository.Update(entity);
        await _repository.SaveChangesAsync();

        await Cache.RemoveAsync(EntityCacheKeys.Post(id), default); // Invalidate chi tiết.
        await _listEpoch.InvalidatePostsListAsync(default); // Invalidate danh sách (metadata có thể đổi).
    } // Kết thúc UpdateAsAuthorAsync.

    // [5] PUT /api/admin/posts/{id} — admin có thể đổi UserId nếu gửi dto.UserId và user đích tồn tại.
    public async Task UpdateAsAdminAsync(Guid id, AdminUpdatePostDto dto)
    { // Mở khối UpdateAsAdminAsync.
        // BƯỚC 1 — Tải entity; null → 404.
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.PostNotFound,
                ApiMessages.PostNotFound);
        }

        // BƯỚC 2 — Nếu có UserId mới — kiểm tra tồn tại rồi gán.
        if (dto.UserId is { } u)
        {
            if (!await _userRepository.ExistsAsync(u))
            {
                throw new ApiException(
                    StatusCodes.Status404NotFound,
                    ApiErrorCodes.UserNotFound,
                    ApiMessages.UserNotFound);
            }

            entity.UserId = u; // Đổi chủ bài.
        }

        // BƯỚC 3 — Áp tiêu đề/nội dung, Save; invalidate cache chi tiết + epoch list.
        entity.Title = dto.Title;
        entity.Content = dto.Content;
        _repository.Update(entity);
        await _repository.SaveChangesAsync();

        await Cache.RemoveAsync(EntityCacheKeys.Post(id), default);
        await _listEpoch.InvalidatePostsListAsync(default);
    } // Kết thúc UpdateAsAdminAsync.

    // [6] DELETE /api/posts/{id} — xóa cache trước rồi Remove entity (cascade comment tùy model).
    public async Task DeleteAsync(Guid id)
    { // Mở khối DeleteAsync.
        // BƯỚC 1 — Tải entity; null → 404.
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.PostNotFound,
                ApiMessages.PostNotFound);
        }

        // BƯỚC 2 — Xóa cache chi tiết + mọi biến thể resource comment theo post (CTE tree/flat).
        await Cache.RemoveAsync(EntityCacheKeys.Post(id), default);
        await Cache.RemoveManyAsync(EntityCacheKeys.PostsResourceCommentsCteAllKeys(id), default); // Khóa l:posts:{id}:comments:*.

        // BƯỚC 3 — Remove + SaveChanges (DB cascade comment nếu cấu hình OnDelete Cascade).
        _repository.Remove(entity);
        await _repository.SaveChangesAsync();

        // BƯỚC 4 — Bump epoch post list + comment list (xóa post làm đổi aggregate lớn).
        await _listEpoch.InvalidatePostsListAsync(default);
        await _listEpoch.InvalidateCommentsListsAsync(default);
    } // Kết thúc DeleteAsync.

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
} // Kết thúc lớp PostService.
