using CommentAPI.DTOs.Comments;

namespace CommentAPI.Services;

public interface ICommentService
{
    Task<List<CommentDto>> GetAllAsync();
    Task<CommentDto?> GetByIdAsync(Guid id);
    Task<CommentDto?> CreateAsync(CreateCommentDto dto);
    Task<bool> UpdateAsync(Guid id, UpdateCommentDto dto);
    Task<bool> DeleteAsync(Guid id);

    Task<List<CommentDto>> GetFlatByPostIdAsync(Guid postId);
    Task<List<CommentTreeDto>> GetTreeByPostIdAsync(Guid postId);
    Task<List<CommentFlatDto>> GetTreeByPostIdCteAsync(Guid postId);
}
