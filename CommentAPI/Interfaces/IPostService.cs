using CommentAPI.DTOs;

namespace CommentAPI.Interfaces;

public interface IPostService
{
    Task<List<PostDto>> GetAllAsync();
    Task<PostDto> GetByIdAsync(Guid id);
    Task<PostDto> CreateAsync(CreatePostDto dto);
    Task UpdateAsync(Guid id, UpdatePostDto dto);
    Task DeleteAsync(Guid id);
}
