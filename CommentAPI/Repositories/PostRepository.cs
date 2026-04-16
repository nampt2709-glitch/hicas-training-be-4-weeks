using CommentAPI.Entities;
using CommentAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Repositories;

public class PostRepository : IPostRepository
{
    private readonly AppDbContext _context;

    public PostRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<Post>> GetAllAsync()
    {
        return await _context.Posts
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<Post?> GetByIdAsync(Guid id)
    {
        return await _context.Posts.FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task AddAsync(Post post)
    {
        await _context.Posts.AddAsync(post);
    }

    public void Update(Post post)
    {
        _context.Posts.Update(post);
    }

    public void Remove(Post post)
    {
        _context.Posts.Remove(post);
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _context.Posts.AsNoTracking().AnyAsync(x => x.Id == id);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}
