using CommentAPI.DTOs;

namespace CommentAPI.Interfaces;

public interface IUserService
{
    Task<List<UserDto>> GetAllAsync();
    Task<UserDto> GetByIdAsync(Guid id);
    Task<UserDto> CreateAsync(CreateUserDto dto);
    Task UpdateAsync(Guid id, UpdateUserDto dto);
    Task DeleteAsync(Guid id);
}
