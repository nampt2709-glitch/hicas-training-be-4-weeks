using AutoMapper;
using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Entities;
using CommentAPI.Interfaces;
using Microsoft.AspNetCore.Http;

namespace CommentAPI.Services;

public class PostService : IPostService
{
    private readonly IPostRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly IMapper _mapper;
    private readonly IEntityResponseCache _cache;

    public PostService(
        IPostRepository repository,
        IUserRepository userRepository,
        IMapper mapper,
        IEntityResponseCache cache)
    {
        _repository = repository;
        _userRepository = userRepository;
        _mapper = mapper;
        _cache = cache;
    }

    public async Task<PagedResult<PostDto>> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = EntityCacheKeys.PostsPaged(page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<PostDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (items, total) = await _repository.GetPagedAsync(page, pageSize, cancellationToken);
        var result = new PagedResult<PostDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken);
        return result;
    }

    public async Task<PagedResult<PostDto>> SearchByTitlePagedAsync(
        string? title,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var term = RequireSearchTerm(title);
        var cacheKey = EntityCacheKeys.PostsSearchTitle(EntityCacheHash.SearchTerm(term), page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<PostDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (items, total) = await _repository.SearchByTitlePagedAsync(term, page, pageSize, cancellationToken);
        var result = new PagedResult<PostDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken);
        return result;
    }

    private static string RequireSearchTerm(string? raw)
    {
        var t = raw?.Trim();
        if (string.IsNullOrEmpty(t))
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.SearchTermRequired,
                ApiMessages.SearchTermRequired);
        }

        return t;
    }

    public async Task<PostDto> GetByIdAsync(Guid id)
    {
        var cacheKey = EntityCacheKeys.Post(id);
        var cached = await _cache.GetJsonAsync<PostDto>(cacheKey, default);
        if (cached is not null)
        {
            return cached;
        }

        var dto = await _repository.GetByIdForReadAsync(id, default);
        if (dto is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.PostNotFound,
                ApiMessages.PostNotFound);
        }

        await _cache.SetJsonAsync(cacheKey, dto, default);
        return dto;
    }

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

        return _mapper.Map<PostDto>(entity);
    }

    public async Task UpdateAsync(Guid id, UpdatePostDto dto)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.PostNotFound,
                ApiMessages.PostNotFound);
        }

        entity.Title = dto.Title;
        entity.Content = dto.Content;
        _repository.Update(entity);
        await _repository.SaveChangesAsync();

        await _cache.RemoveAsync(EntityCacheKeys.Post(id), default);
    }

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

        await _cache.RemoveAsync(EntityCacheKeys.Post(id), default);

        _repository.Remove(entity);
        await _repository.SaveChangesAsync();
    }
}
