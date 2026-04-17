using CommentAPI.Entities;

namespace CommentAPI.Interfaces;

public interface IUserRepository
{
    Task<List<User>> GetAllAsync();
    Task<User?> GetByIdAsync(Guid id);
    Task AddAsync(User user);
    void Update(User user);
    void Remove(User user);
    Task<bool> ExistsAsync(Guid id);
    Task SaveChangesAsync();
}
