using ApartmentAPI.V1.DTOs;
using ApartmentAPI.Entities;
using AutoMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ApartmentAPI.Services;

// Vai trò Identity (Role): CRUD; cập nhật chỉ mô tả (tên role đổi ít khi qua API đơn giản).
public interface IRoleAppService
{
    Task<List<RoleDto>> GetAllAsync(CancellationToken ct = default);
    Task<RoleDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RoleDto> CreateAsync(CreateRoleDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateRoleDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

// RoleManager<Role>: tạo/xóa role; cập nhật Description trên entity Role tùy chỉnh.
public sealed class RoleAppService : IRoleAppService
{
    private readonly RoleManager<Role> _roles;
    private readonly IMapper _mapper;

    public RoleAppService(RoleManager<Role> roles, IMapper mapper)
    {
        _roles = roles;
        _mapper = mapper;
    }

    public async Task<List<RoleDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _roles.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        return _mapper.Map<List<RoleDto>>(list);
    }

    public async Task<RoleDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var role = await _roles.FindByIdAsync(id.ToString());
        if (role is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Role not found.");
        return _mapper.Map<RoleDto>(role);
    }

    public async Task<RoleDto> CreateAsync(CreateRoleDto dto, CancellationToken ct = default)
    {
        var role = new Role { Name = dto.Name, Description = dto.Description };
        var result = await _roles.CreateAsync(role);
        if (!result.Succeeded)
        {
            var msg = string.Join("; ", result.Errors.Select(e => e.Description));
            throw ApiException.BadRequest(ApiErrorCodes.Validation, msg);
        }

        return _mapper.Map<RoleDto>(role);
    }

    public async Task UpdateAsync(Guid id, UpdateRoleDto dto, CancellationToken ct = default)
    {
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
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var role = await _roles.FindByIdAsync(id.ToString());
        if (role is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Role not found.");

        var result = await _roles.DeleteAsync(role);
        if (!result.Succeeded)
        {
            var msg = string.Join("; ", result.Errors.Select(e => e.Description));
            throw ApiException.BadRequest(ApiErrorCodes.Validation, msg);
        }
    }
}
