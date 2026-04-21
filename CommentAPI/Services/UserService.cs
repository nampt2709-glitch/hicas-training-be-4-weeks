using AutoMapper;
using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Entities;
using CommentAPI.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace CommentAPI.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _repository;
    private readonly UserManager<User> _userManager;
    private readonly IMapper _mapper;
    private readonly IEntityResponseCache _cache;

    public UserService(
        IUserRepository repository,
        UserManager<User> userManager,
        IMapper mapper,
        IEntityResponseCache cache)
    {
        _repository = repository;
        _userManager = userManager;
        _mapper = mapper;
        _cache = cache;
    }

    public async Task<PagedResult<UserDto>> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = EntityCacheKeys.UsersPaged(page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<UserDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        // Lấy một trang user và map kèm vai trò (giữ nguyên cách MapToDtoAsync).
        var (items, total) = await _repository.GetPagedAsync(page, pageSize, cancellationToken);
        var ids = items.ConvertAll(x => x.Id);
        var rolesByUser = await _repository.GetRoleNamesByUserIdsAsync(ids, cancellationToken);
        var list = new List<UserDto>(items.Count);
        foreach (var row in items)
        {
            list.Add(ToUserDto(row, rolesByUser));
        }

        var result = new PagedResult<UserDto>
        {
            Items = list,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken);
        return result;
    }

    public async Task<PagedResult<UserDto>> SearchByNamePagedAsync(
        string? name,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var term = RequireSearchTerm(name);
        var cacheKey = EntityCacheKeys.UsersSearchName(EntityCacheHash.SearchTerm(term), page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<UserDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (items, total) = await _repository.SearchByNamePagedAsync(term, page, pageSize, cancellationToken);
        var ids = items.ConvertAll(x => x.Id);
        var rolesByUser = await _repository.GetRoleNamesByUserIdsAsync(ids, cancellationToken);
        var list = new List<UserDto>(items.Count);
        foreach (var row in items)
        {
            list.Add(ToUserDto(row, rolesByUser));
        }

        var result = new PagedResult<UserDto>
        {
            Items = list,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken);
        return result;
    }

    public async Task<PagedResult<UserDto>> SearchByUserNamePagedAsync(
        string? userName,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var term = RequireSearchTerm(userName);
        var cacheKey = EntityCacheKeys.UsersSearchUserName(EntityCacheHash.SearchTerm(term), page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<UserDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (items, total) = await _repository.SearchByUserNamePagedAsync(term, page, pageSize, cancellationToken);
        var ids = items.ConvertAll(x => x.Id);
        var rolesByUser = await _repository.GetRoleNamesByUserIdsAsync(ids, cancellationToken);
        var list = new List<UserDto>(items.Count);
        foreach (var row in items)
        {
            list.Add(ToUserDto(row, rolesByUser));
        }

        var result = new PagedResult<UserDto>
        {
            Items = list,
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

    public async Task<UserDto> GetByIdAsync(Guid id)
    {
        // Cache-aside: đọc DTO từ Redis/memory trước; miss thì truy DB rồi ghi lại cache.
        var cacheKey = EntityCacheKeys.User(id);
        var cached = await _cache.GetJsonAsync<UserDto>(cacheKey, CancellationToken.None);
        if (cached is not null)
        {
            return cached;
        }

        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.UserNotFound,
                ApiMessages.UserNotFound);
        }

        var dto = await MapToDtoAsync(entity);
        await _cache.SetJsonAsync(cacheKey, dto, default);
        return dto;
    }

    public async Task<UserDto> CreateAsync(CreateUserDto dto)
    {
        if (await _userManager.FindByNameAsync(dto.UserName) != null)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.UserNameConflict,
                ApiMessages.UserNameTaken);
        }

        var email = string.IsNullOrWhiteSpace(dto.Email)
            ? $"{dto.UserName}@users.local"
            : dto.Email!.Trim();

        var entity = new User
        {
            Id = Guid.NewGuid(),
            UserName = dto.UserName,
            Name = dto.Name,
            Email = email,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(entity, dto.Password);
        if (!result.Succeeded)
        {
            var detail = string.Join(" ", result.Errors.Select(e => e.Description));
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.UserCreateFailed,
                string.IsNullOrWhiteSpace(detail) ? ApiMessages.UserCreateFailed : detail);
        }

        await _userManager.AddToRoleAsync(entity, "User");
        return await MapToDtoAsync(entity);
    }

    public async Task UpdateAsync(Guid id, UpdateUserDto dto)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.UserNotFound,
                ApiMessages.UserNotFound);
        }

        entity.Name = dto.Name;
        _repository.Update(entity);
        await _repository.SaveChangesAsync();

        // Dữ liệu user đổi — xóa cache theo id để lần sau không trả bản cũ.
        await _cache.RemoveAsync(EntityCacheKeys.User(id), default);
    }

    public async Task DeleteAsync(Guid id)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.UserNotFound,
                ApiMessages.UserNotFound);
        }

        await _cache.RemoveAsync(EntityCacheKeys.User(id), default);

        var result = await _userManager.DeleteAsync(entity);
        if (!result.Succeeded)
        {
            var detail = string.Join(" ", result.Errors.Select(e => e.Description));
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.UserDeleteFailed,
                string.IsNullOrWhiteSpace(detail) ? ApiMessages.UserDeleteFailed : detail);
        }
    }

    private async Task<UserDto> MapToDtoAsync(User entity)
    {
        var dto = _mapper.Map<UserDto>(entity);
        var roles = await _userManager.GetRolesAsync(entity);
        dto.Roles = roles.OrderBy(r => r).ToList();
        return dto;
    }

    /// <summary>Ghép hàng projection <see cref="UserPageRow"/> với role đã batch-load.</summary>
    private static UserDto ToUserDto(UserPageRow row, IReadOnlyDictionary<Guid, List<string>> rolesByUser)
    {
        return new UserDto
        {
            Id = row.Id,
            Name = row.Name,
            UserName = row.UserName,
            Email = row.Email,
            CreatedAt = row.CreatedAt,
            Roles = rolesByUser.TryGetValue(row.Id, out var r) ? r : new List<string>()
        };
    }
}
