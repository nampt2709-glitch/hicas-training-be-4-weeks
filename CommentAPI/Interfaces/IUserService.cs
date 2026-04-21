using CommentAPI.DTOs;

namespace CommentAPI.Interfaces;

public interface IUserService
{
    Task<PagedResult<UserDto>> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Tìm user theo <see cref="UserDto.Name"/> (chuỗi chứa), có phân trang.</summary>
    Task<PagedResult<UserDto>> SearchByNamePagedAsync(
        string? name,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Tìm user theo <see cref="UserDto.UserName"/> (chuỗi chứa), có phân trang.</summary>
    Task<PagedResult<UserDto>> SearchByUserNamePagedAsync(
        string? userName,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<UserDto> GetByIdAsync(Guid id);
    Task<UserDto> CreateAsync(CreateUserDto dto);
    Task UpdateAsync(Guid id, UpdateUserDto dto);
    Task DeleteAsync(Guid id);
}
