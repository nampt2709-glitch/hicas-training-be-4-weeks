using ApartmentAPI.V1.DTOs;
using ApartmentAPI.Entities;
using AutoMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ApartmentAPI.Services;

// Nghiệp vụ người dùng Identity: CRUD tối giản (không xóa mềm — User không kế thừa BaseEntity).
public interface IUserAppService
{
    Task<List<UserListDto>> GetAllAsync(CancellationToken ct = default);
    Task<UserListDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UserListDto> CreateAsync(CreateUserDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateUserDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

// UserManager<User>: tạo có mật khẩu băm; cập nhật FullName / AvatarUrl / IsActive.
public sealed class UserAppService : IUserAppService
{
    private readonly UserManager<User> _users;
    private readonly IMapper _mapper;

    public UserAppService(UserManager<User> users, IMapper mapper)
    {
        _users = users;
        _mapper = mapper;
    }

    public async Task<List<UserListDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _users.Users.AsNoTracking().OrderBy(u => u.CreatedAt).ToListAsync(ct);
        return _mapper.Map<List<UserListDto>>(list);
    }

    public async Task<UserListDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(id.ToString());
        if (user is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");
        return _mapper.Map<UserListDto>(user);
    }

    public async Task<UserListDto> CreateAsync(CreateUserDto dto, CancellationToken ct = default)
    {
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

        return _mapper.Map<UserListDto>(user);
    }

    public async Task UpdateAsync(Guid id, UpdateUserDto dto, CancellationToken ct = default)
    {
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
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(id.ToString());
        if (user is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");

        var result = await _users.DeleteAsync(user);
        if (!result.Succeeded)
        {
            var msg = string.Join("; ", result.Errors.Select(e => e.Description));
            throw ApiException.BadRequest(ApiErrorCodes.Validation, msg);
        }
    }
}
