using CommentAPI.DTOs.Users;

namespace CommentAPI.Services;

public interface IUserService
{
    Task<List<UserDto>> GetAllAsync();
    Task<UserDto?> GetByIdAsync(Guid id);
    Task<UserDto> CreateAsync(CreateUserDto dto);
    Task<bool> UpdateAsync(Guid id, UpdateUserDto dto);
    Task<bool> DeleteAsync(Guid id);
}
