using AutoMapper;
using CommentAPI.DTOs;
using CommentAPI.Entities;
using CommentAPI.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace CommentAPI.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _repository;
    private readonly UserManager<User> _userManager;
    private readonly IMapper _mapper;

    public UserService(IUserRepository repository, UserManager<User> userManager, IMapper mapper)
    {
        _repository = repository;
        _userManager = userManager;
        _mapper = mapper;
    }

    public async Task<List<UserDto>> GetAllAsync()
    {
        var entities = await _repository.GetAllAsync();
        var list = new List<UserDto>();
        foreach (var entity in entities)
        {
            list.Add(await MapToDtoAsync(entity));
        }

        return list;
    }

    public async Task<UserDto?> GetByIdAsync(Guid id)
    {
        var entity = await _repository.GetByIdAsync(id);
        return entity is null ? null : await MapToDtoAsync(entity);
    }

    public async Task<UserDto?> CreateAsync(CreateUserDto dto)
    {
        if (await _userManager.FindByNameAsync(dto.UserName) != null)
        {
            return null;
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
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        await _userManager.AddToRoleAsync(entity, "User");
        return await MapToDtoAsync(entity);
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateUserDto dto)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            return false;
        }

        entity.Name = dto.Name;
        _repository.Update(entity);
        await _repository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            return false;
        }

        var result = await _userManager.DeleteAsync(entity);
        return result.Succeeded;
    }

    private async Task<UserDto> MapToDtoAsync(User entity)
    {
        var dto = _mapper.Map<UserDto>(entity);
        var roles = await _userManager.GetRolesAsync(entity);
        dto.Roles = roles.OrderBy(r => r).ToList();
        return dto;
    }
}
