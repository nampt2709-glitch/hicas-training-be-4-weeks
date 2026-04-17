using CommentAPI.Entities;

namespace CommentAPI.Interfaces;

public interface IPostRepository
{
    Task<List<Post>> GetAllAsync();
    Task<Post?> GetByIdAsync(Guid id);
    Task AddAsync(Post post);
    void Update(Post post);
    void Remove(Post post);
    Task<bool> ExistsAsync(Guid id);
    Task SaveChangesAsync();
}
