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

    public async Task<UserDto> GetByIdAsync(Guid id)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.UserNotFound,
                ApiMessages.UserNotFound);
        }

        return await MapToDtoAsync(entity);
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
}
