using ApartmentAPI.DTOs; // PagedResult, PaginationQuery.
using ApartmentAPI.V1.DTOs; // UserListDto, Create/Update, SignUpRequestDto.
using ApartmentAPI.Entities; // User.
using AutoMapper; // IMapper.
using Microsoft.AspNetCore.Http; // StatusCodes (409 conflict đăng ký).
using Microsoft.AspNetCore.Identity; // UserManager.
using Microsoft.EntityFrameworkCore; // AsNoTracking, Where, ToListAsync.

namespace ApartmentAPI.Services;

// Nghiệp vụ người dùng Identity: CRUD tối giản (không xóa mềm — User không kế thừa BaseEntity).
public interface IUserService
{
    Task<PagedResult<UserListDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        string? userNameContains,
        string? emailContains,
        string? fullNameContains,
        bool? isActive,
        string? sort,
        string? sortDir,
        CancellationToken ct = default);

    Task<UserListDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UserListDto> CreateAsync(CreateUserDto dto, CancellationToken ct = default);
    Task<UserListDto> SignUpWithDefaultUserRoleAsync(SignUpRequestDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateUserDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

// UserManager<User>: tạo có mật khẩu băm; cập nhật FullName / AvatarUrl / IsActive.
public sealed class UserService : ServiceBase, IUserService
{
    private readonly UserManager<User> _users; // Identity CRUD user.
    private readonly IMapper _mapper; // Map User ↔ UserListDto.

    public UserService(
        UserManager<User> users,
        IMapper mapper,
        IEntityResponseCache cache,
        ICacheListEpochStore listEpoch)
        : base(cache, listEpoch)
    { // Mở khối constructor.
        _users = users;
        _mapper = mapper;
    } // Kết thúc constructor.

    private static bool HasUserListFilter(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        string? userNameContains,
        string? emailContains,
        string? fullNameContains,
        bool? isActive) =>
        HasCreatedAtFilter(createdAtFrom, createdAtTo)
        || HasTextFilter(userNameContains)
        || HasTextFilter(emailContains)
        || HasTextFilter(fullNameContains)
        || isActive.HasValue;

    public async Task<PagedResult<UserListDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        string? userNameContains,
        string? emailContains,
        string? fullNameContains,
        bool? isActive,
        string? sort,
        string? sortDir,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        var sortSpec = ListSortParsing.ParseUserSort(sort, sortDir);

        if (!HasUserListFilter(createdAtFrom, createdAtTo, userNameContains, emailContains, fullNameContains, isActive))
        {
            var epoch = await ListEpoch.GetUsersListEpochAsync(ct);
            var key = EntityCacheKeys.UsersPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            var cached = await Cache.GetJsonAsync<PagedResult<UserListDto>>(key, ct);
            if (cached is not null)
                return cached;
        }

        var (p, s) = PaginationQuery.Normalize(page, pageSize);
        var q = _users.Users.AsNoTracking().AsQueryable();
        if (createdAtFrom is { } f)
            q = q.Where(u => u.CreatedAt >= f);
        if (createdAtTo is { } t)
            q = q.Where(u => u.CreatedAt <= t);
        var un = userNameContains?.Trim();
        if (!string.IsNullOrEmpty(un))
            q = q.Where(u => u.UserName != null && u.UserName.Contains(un));
        var em = emailContains?.Trim();
        if (!string.IsNullOrEmpty(em))
            q = q.Where(u => u.Email != null && u.Email.Contains(em));
        var fn = fullNameContains?.Trim();
        if (!string.IsNullOrEmpty(fn))
            q = q.Where(u => u.FullName.Contains(fn));
        if (isActive is { } act)
            q = q.Where(u => u.IsActive == act);

        var total = await q.LongCountAsync(ct);
        q = ApplyUserSort(q, sortSpec);
        var list = await q.Skip((p - 1) * s).Take(s).ToListAsync(ct);
        var dtos = _mapper.Map<List<UserListDto>>(list);
        var result = new PagedResult<UserListDto> { Items = dtos, Page = p, PageSize = s, TotalCount = total };

        if (!HasUserListFilter(createdAtFrom, createdAtTo, userNameContains, emailContains, fullNameContains, isActive))
        {
            var epoch = await ListEpoch.GetUsersListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.UsersPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            await Cache.SetJsonAsync(cacheKey, result, ct);
        }

        return result;
    } // Kết thúc GetPagedAsync.

    private static IQueryable<User> ApplyUserSort(IQueryable<User> q, UserListSort spec)
    { // Mở khối ApplyUserSort.
        var desc = spec.Descending;
        return spec.Column switch
        {
            UserSortColumn.Id => desc ? q.OrderByDescending(u => u.Id) : q.OrderBy(u => u.Id),
            UserSortColumn.UserName => desc ? q.OrderByDescending(u => u.UserName) : q.OrderBy(u => u.UserName),
            UserSortColumn.Email => desc ? q.OrderByDescending(u => u.Email) : q.OrderBy(u => u.Email),
            UserSortColumn.FullName => desc ? q.OrderByDescending(u => u.FullName) : q.OrderBy(u => u.FullName),
            UserSortColumn.IsActive => desc ? q.OrderByDescending(u => u.IsActive) : q.OrderBy(u => u.IsActive),
            UserSortColumn.CreatedAt => desc ? q.OrderByDescending(u => u.CreatedAt) : q.OrderBy(u => u.CreatedAt),
            _ => desc ? q.OrderByDescending(u => u.CreatedAt) : q.OrderBy(u => u.CreatedAt),
        };
    } // Kết thúc ApplyUserSort.

    public async Task<UserListDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        var cacheKey = EntityCacheKeys.User(id);
        var cached = await Cache.GetJsonAsync<UserListDto>(cacheKey, ct);
        if (cached is not null)
            return cached;

        var user = await _users.FindByIdAsync(id.ToString());
        if (user is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");
        var dto = _mapper.Map<UserListDto>(user);
        await Cache.SetJsonAsync(cacheKey, dto, ct);
        return dto;
    } // Kết thúc GetByIdAsync.

    public async Task<UserListDto> SignUpWithDefaultUserRoleAsync(SignUpRequestDto dto, CancellationToken ct = default)
    { // Mở khối SignUpWithDefaultUserRoleAsync — luồng public đăng ký + gán role "User".
        if (await _users.FindByNameAsync(dto.UserName) != null)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.Conflict,
                "Username already taken.");
        }

        var email = string.IsNullOrWhiteSpace(dto.Email)
            ? $"{dto.UserName}@users.local"
            : dto.Email.Trim();

        var user = new User
        {
            UserName = dto.UserName,
            Email = email,
            FullName = dto.Name,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
        };

        var result = await _users.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            var msg = string.Join("; ", result.Errors.Select(e => e.Description));
            throw ApiException.BadRequest(ApiErrorCodes.Validation, msg);
        }

        await _users.AddToRoleAsync(user, "User");
        await ListEpoch.InvalidateUsersListsAsync(ct);
        return _mapper.Map<UserListDto>(user);
    } // Kết thúc SignUpWithDefaultUserRoleAsync.

    public async Task<UserListDto> CreateAsync(CreateUserDto dto, CancellationToken ct = default)
    { // Mở khối CreateAsync — tạo user admin/script với field đầy đủ từ DTO.
        var user = new User
        {
            UserName = dto.UserName,
            Email = dto.Email,
            FullName = dto.FullName,
        };
        var result = await _users.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            var msg = string.Join("; ", result.Errors.Select(e => e.Description));
            throw ApiException.BadRequest(ApiErrorCodes.Validation, msg);
        }

        await Cache.RemoveAsync(EntityCacheKeys.User(user.Id), ct);
        await ListEpoch.InvalidateUsersListsAsync(ct);
        return _mapper.Map<UserListDto>(user);
    } // Kết thúc CreateAsync.

    public async Task UpdateAsync(Guid id, UpdateUserDto dto, CancellationToken ct = default)
    { // Mở khối UpdateAsync.
        var user = await _users.FindByIdAsync(id.ToString());
        if (user is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");

        user.FullName = dto.FullName;
        user.AvatarUrl = dto.AvatarUrl;
        user.IsActive = dto.IsActive;
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var msg = string.Join("; ", result.Errors.Select(e => e.Description));
            throw ApiException.BadRequest(ApiErrorCodes.Validation, msg);
        }

        await Cache.RemoveAsync(EntityCacheKeys.User(id), ct);
        await ListEpoch.InvalidateUsersListsAsync(ct);
    } // Kết thúc UpdateAsync.

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    { // Mở khối DeleteAsync — xóa cứng user.
        var user = await _users.FindByIdAsync(id.ToString());
        if (user is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");

        var result = await _users.DeleteAsync(user);
        if (!result.Succeeded)
        {
            var msg = string.Join("; ", result.Errors.Select(e => e.Description));
            throw ApiException.BadRequest(ApiErrorCodes.Validation, msg);
        }

        await Cache.RemoveAsync(EntityCacheKeys.User(id), ct);
        await ListEpoch.InvalidateUsersListsAsync(ct);
    } // Kết thúc DeleteAsync.
}
