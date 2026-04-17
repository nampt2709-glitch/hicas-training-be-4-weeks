using CommentAPI.DTOs;
using CommentAPI.Entities;

namespace CommentAPI.Interfaces;

public interface ICommentRepository
{
    Task<List<Comment>> GetAllAsync();
    Task<List<Comment>> GetByPostIdAsync(Guid postId);
    Task<Comment?> GetByIdAsync(Guid id);
    Task AddAsync(Comment comment);
    void Update(Comment comment);
    void Remove(Comment comment);
    Task<bool> ExistsAsync(Guid id);
    Task<bool> PostExistsAsync(Guid postId);
    Task<bool> UserExistsAsync(Guid userId);
    Task<bool> ParentExistsAsync(Guid parentId, Guid postId);
    Task<List<CommentFlatDto>> GetTreeRowsByCteAsync(Guid postId);

    /// <summary>CTE đệ quy trên toàn bộ comment (mọi post); join ParentId kèm PostId.</summary>
    Task<List<CommentFlatDto>> GetTreeRowsByCteAllAsync();

    Task SaveChangesAsync();
}
