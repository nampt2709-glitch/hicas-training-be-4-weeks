using CommentAPI.DTOs.Posts;

namespace CommentAPI.Services;

public interface IPostService
{
    Task<List<PostDto>> GetAllAsync();
    Task<PostDto?> GetByIdAsync(Guid id);
    Task<PostDto> CreateAsync(CreatePostDto dto);
    Task<bool> UpdateAsync(Guid id, UpdatePostDto dto);
    Task<bool> DeleteAsync(Guid id);
}
