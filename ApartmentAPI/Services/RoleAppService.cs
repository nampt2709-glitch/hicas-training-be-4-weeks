using ApartmentAPI.DTOs; // PagedResult, PaginationQuery.
using ApartmentAPI.V1.DTOs; // RoleDto, Create/Update.
using ApartmentAPI.Entities; // Role.
using AutoMapper; // IMapper.
using Microsoft.AspNetCore.Identity; // RoleManager.
using Microsoft.EntityFrameworkCore; // AsNoTracking, ToListAsync, LongCountAsync.

namespace ApartmentAPI.Services;

// Vai trò Identity (Role): danh sách phân trang + cache khi không filter; CRUD.
public interface IRoleAppService
{
    Task<PagedResult<RoleDto>> GetPagedAsync(
        int page,
        int pageSize,
        string? nameContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default);

    Task<RoleDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RoleDto> CreateAsync(CreateRoleDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateRoleDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

// RoleManager<Role>: tạo/xóa role; cập nhật Description trên entity Role tùy chỉnh.
public sealed class RoleAppService : IRoleAppService
{
    private readonly RoleManager<Role> _roles; // Truy cập bảng Roles qua Identity.
    private readonly IMapper _mapper; // Role ↔ RoleDto.
    private readonly IEntityResponseCache _cache; // Cache list/chi tiết.
    private readonly ICacheListEpochStore _listEpoch; // Epoch danh sách role.

    public RoleAppService(
        RoleManager<Role> roles,
        IMapper mapper,
        IEntityResponseCache cache,
        ICacheListEpochStore listEpoch)
    { // Mở khối constructor.
        _roles = roles;
        _mapper = mapper;
        _cache = cache;
        _listEpoch = listEpoch;
    } // Kết thúc constructor.

    private static bool HasRoleListFilter(string? nameContains) => !string.IsNullOrWhiteSpace(nameContains);

    public async Task<PagedResult<RoleDto>> GetPagedAsync(
        int page,
        int pageSize,
        string? nameContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync — query trực tiếp IQueryable Roles (không qua repository riêng).
        var sortSpec = ListSortParsing.ParseRoleSort(sort, sortDir);

        if (!HasRoleListFilter(nameContains))
        {
            var epoch = await _listEpoch.GetRolesListEpochAsync(ct);
            var key = EntityCacheKeys.RolesPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            var cached = await _cache.GetJsonAsync<PagedResult<RoleDto>>(key, ct);
            if (cached is not null)
                return cached;
        }

        var (p, s) = PaginationQuery.Normalize(page, pageSize);
        var q = _roles.Roles.AsNoTracking().AsQueryable();
        var nm = nameContains?.Trim();
        if (!string.IsNullOrEmpty(nm))
            q = q.Where(r => r.Name != null && r.Name.Contains(nm));

        var total = await q.LongCountAsync(ct);
        q = ApplyRoleSort(q, sortSpec);
        var list = await q.Skip((p - 1) * s).Take(s).ToListAsync(ct);
        var dtos = _mapper.Map<List<RoleDto>>(list);
        var result = new PagedResult<RoleDto> { Items = dtos, Page = p, PageSize = s, TotalCount = total };

        if (!HasRoleListFilter(nameContains))
        {
            var epoch = await _listEpoch.GetRolesListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.RolesPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            await _cache.SetJsonAsync(cacheKey, result, ct);
        }

        return result;
    } // Kết thúc GetPagedAsync.

    private static IQueryable<Role> ApplyRoleSort(IQueryable<Role> q, RoleListSort spec)
    { // Mở khối ApplyRoleSort — switch cột + hướng giảm.
        var desc = spec.Descending;
        return spec.Column switch
        {
            RoleSortColumn.Id => desc ? q.OrderByDescending(r => r.Id) : q.OrderBy(r => r.Id),
            RoleSortColumn.Name => desc ? q.OrderByDescending(r => r.Name) : q.OrderBy(r => r.Name),
            RoleSortColumn.Description => desc
                ? q.OrderByDescending(r => r.Description).ThenBy(r => r.Name)
                : q.OrderBy(r => r.Description).ThenBy(r => r.Name),
            _ => desc ? q.OrderByDescending(r => r.Name) : q.OrderBy(r => r.Name),
        };
    } // Kết thúc ApplyRoleSort.

    public async Task<RoleDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        var key = EntityCacheKeys.Role(id);
        var cached = await _cache.GetJsonAsync<RoleDto>(key, ct);
        if (cached is not null)
            return cached;

        var role = await _roles.FindByIdAsync(id.ToString());
        if (role is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Role not found.");
        var dto = _mapper.Map<RoleDto>(role);
        await _cache.SetJsonAsync(key, dto, ct);
        return dto;
    } // Kết thúc GetByIdAsync.

    public async Task<RoleDto> CreateAsync(CreateRoleDto dto, CancellationToken ct = default)
    { // Mở khối CreateAsync.
        var role = new Role { Name = dto.Name, Description = dto.Description };
        var result = await _roles.CreateAsync(role);
        if (!result.Succeeded)
        {
            var msg = string.Join("; ", result.Errors.Select(e => e.Description));
            throw ApiException.BadRequest(ApiErrorCodes.Validation, msg);
        }

        var roleDto = _mapper.Map<RoleDto>(role);
        await _cache.SetJsonAsync(EntityCacheKeys.Role(role.Id), roleDto, ct);
        await _listEpoch.InvalidateRolesListsAsync(ct);
        return roleDto;
    } // Kết thúc CreateAsync.

    public async Task UpdateAsync(Guid id, UpdateRoleDto dto, CancellationToken ct = default)
    { // Mở khối UpdateAsync — chỉ Description (Name đổi qua Identity phức tạp hơn, không nằm trong DTO).
        var role = await _roles.FindByIdAsync(id.ToString());
        if (role is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Role not found.");

        role.Description = dto.Description;
        var result = await _roles.UpdateAsync(role);
        if (!result.Succeeded)
        {
            var msg = string.Join("; ", result.Errors.Select(e => e.Description));
            throw ApiException.BadRequest(ApiErrorCodes.Validation, msg);
        }

        await _cache.RemoveAsync(EntityCacheKeys.Role(id), ct);
        await _listEpoch.InvalidateRolesListsAsync(ct);
    } // Kết thúc UpdateAsync.

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    { // Mở khối DeleteAsync — xóa cứng role trong Identity.
        var role = await _roles.FindByIdAsync(id.ToString());
        if (role is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Role not found.");

        var result = await _roles.DeleteAsync(role);
        if (!result.Succeeded)
        {
            var msg = string.Join("; ", result.Errors.Select(e => e.Description));
            throw ApiException.BadRequest(ApiErrorCodes.Validation, msg);
        }

        await _cache.RemoveAsync(EntityCacheKeys.Role(id), ct);
        await _listEpoch.InvalidateRolesListsAsync(ct);
    } // Kết thúc DeleteAsync.
}
