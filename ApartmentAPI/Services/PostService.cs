using ApartmentAPI.DTOs; // PagedResult.
using ApartmentAPI.V1.DTOs; // Post DTO.
using ApartmentAPI.Entities; // Post, User.
using ApartmentAPI.Repositories; // IPostRepository, IApartmentRepository.
using AutoMapper; // IMapper.
using Microsoft.AspNetCore.Identity; // UserManager.

namespace ApartmentAPI.Services;

// Nghiệp vụ bài đăng / thông báo: phân trang + cache khi không filter; CRUD mềm.
public interface IPostService
{
    Task<PagedResult<PostDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        Guid? apartmentId,
        bool? isPublished,
        string? titleContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default);

    Task<PostDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PostDto> CreateAsync(CreatePostDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdatePostDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

// CRUD Post: UserId và ApartmentId (tuỳ chọn) phải hợp lệ khi tạo/cập nhật.
public sealed class PostService : ServiceBase, IPostService
{
    private readonly IPostRepository _repository;
    private readonly UserManager<User> _users;
    private readonly IApartmentRepository _apartments;
    private readonly IMapper _mapper;

    public PostService(
        IPostRepository repository,
        UserManager<User> users,
        IApartmentRepository apartments,
        IMapper mapper,
        IEntityResponseCache cache,
        ICacheListEpochStore listEpoch)
        : base(cache, listEpoch)
    {
        _repository = repository;
        _users = users;
        _apartments = apartments;
        _mapper = mapper;
    }

    private static bool HasListFilter(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        Guid? apartmentId,
        bool? isPublished,
        string? titleContains) =>
        HasCreatedAtFilter(createdAtFrom, createdAtTo)
        || userId.HasValue
        || apartmentId.HasValue
        || isPublished.HasValue
        || HasTextFilter(titleContains);

    public async Task<PagedResult<PostDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        Guid? apartmentId,
        bool? isPublished,
        string? titleContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default)
    {
        var sortSpec = ListSortParsing.ParsePostSort(sort, sortDir);

        if (!HasListFilter(createdAtFrom, createdAtTo, userId, apartmentId, isPublished, titleContains))
        {
            var epoch = await ListEpoch.GetPostsListEpochAsync(ct);
            var key = EntityCacheKeys.PostsPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            var cached = await Cache.GetJsonAsync<PagedResult<PostDto>>(key, ct);
            if (cached is not null)
                return cached;
        }

        var (items, total, p, s) = await _repository.GetPagedAsync(
            page, pageSize, createdAtFrom, createdAtTo, userId, apartmentId, isPublished, titleContains, sortSpec, ct);

        var dtos = _mapper.Map<List<PostDto>>(items);
        var result = new PagedResult<PostDto> { Items = dtos, Page = p, PageSize = s, TotalCount = total };

        if (!HasListFilter(createdAtFrom, createdAtTo, userId, apartmentId, isPublished, titleContains))
        {
            var epoch = await ListEpoch.GetPostsListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.PostsPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            await Cache.SetJsonAsync(cacheKey, result, ct);
        }

        return result;
    }

    public async Task<PostDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var key = EntityCacheKeys.Post(id);
        var cached = await Cache.GetJsonAsync<PostDto>(key, ct);
        if (cached is not null)
            return cached;

        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Post not found.");
        var dto = _mapper.Map<PostDto>(entity);
        await Cache.SetJsonAsync(key, dto, ct);
        return dto;
    }

    public async Task<PostDto> CreateAsync(CreatePostDto dto, CancellationToken ct = default)
    {
        if (await _users.FindByIdAsync(dto.UserId.ToString()) is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");
        if (dto.ApartmentId is { } aptId && !await _apartments.ExistsAsync(aptId, ct))
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Apartment not found.");

        var entity = _mapper.Map<Post>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        var dtoOut = _mapper.Map<PostDto>(entity);
        await Cache.SetJsonAsync(EntityCacheKeys.Post(entity.Id), dtoOut, ct);
        await ListEpoch.InvalidatePostsListsAsync(ct);
        return dtoOut;
    }

    public async Task UpdateAsync(Guid id, UpdatePostDto dto, CancellationToken ct = default)
    {
        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Post not found.");
        if (dto.ApartmentId is { } aptId && !await _apartments.ExistsAsync(aptId, ct))
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Apartment not found.");

        _mapper.Map(dto, tracked);
        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
        await Cache.RemoveAsync(EntityCacheKeys.Post(id), ct);
        await ListEpoch.InvalidatePostsListsAsync(ct);
    }

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    {
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
        await Cache.RemoveAsync(EntityCacheKeys.Post(id), ct);
        await ListEpoch.InvalidatePostsListsAsync(ct);
    }
}
