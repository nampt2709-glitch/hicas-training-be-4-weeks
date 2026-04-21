using CommentAPI.Entities;
using CommentAPI.Data;
using CommentAPI.DTOs;
using CommentAPI.Interfaces;
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

    public async Task<(List<PostDto> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var q = _context.Posts.AsNoTracking();
        var total = await q.LongCountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PostDto
            {
                Id = p.Id,
                Title = p.Title,
                Content = p.Content,
                CreatedAt = p.CreatedAt,
                UserId = p.UserId
            })
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<(List<PostDto> Items, long TotalCount)> SearchByTitlePagedAsync(
        string titleContains,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var q = _context.Posts.AsNoTracking().Where(p => p.Title.Contains(titleContains));
        var total = await q.LongCountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PostDto
            {
                Id = p.Id,
                Title = p.Title,
                Content = p.Content,
                CreatedAt = p.CreatedAt,
                UserId = p.UserId
            })
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<PostDto?> GetByIdForReadAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Posts.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PostDto
            {
                Id = p.Id,
                Title = p.Title,
                Content = p.Content,
                CreatedAt = p.CreatedAt,
                UserId = p.UserId
            })
            .FirstOrDefaultAsync(cancellationToken);

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
