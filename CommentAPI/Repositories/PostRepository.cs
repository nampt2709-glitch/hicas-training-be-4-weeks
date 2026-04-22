using CommentAPI.Entities; 
using CommentAPI.Data; 
using CommentAPI.DTOs; 
using CommentAPI.Interfaces;
using Microsoft.EntityFrameworkCore; 

namespace CommentAPI.Repositories; 

public class PostRepository : IPostRepository // CRUD + read projections.
{
    private readonly AppDbContext _context; // Database context.

    public PostRepository(AppDbContext context) // DI.
    {
        _context = context; // Assign.
    }

    public async Task<List<Post>> GetAllAsync() // Full list entities (legacy/helper).
    {
        return await _context.Posts // DbSet.
            .AsNoTracking() // Read-only.
            .OrderByDescending(x => x.CreatedAt) // Newest first.
            .ToListAsync(); // Materialize.
    }

    public async Task<(List<PostDto> Items, long TotalCount)> GetPagedAsync( // Paged list as DTO projection.
        int page, // Page (caller normalized in some paths).
        int pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var q = _context.Posts.AsNoTracking(); // Base query.
        var total = await q.LongCountAsync(cancellationToken); // Count all posts.
        var items = await q // Execute page.
            .OrderByDescending(p => p.CreatedAt) // Newest first.
            .ThenBy(p => p.Id) // Stable order.
            .Skip((page - 1) * pageSize) // Offset — assumes normalized page/size upstream for this repo.
            .Take(pageSize) // Limit.
            .Select(p => new PostDto // Project to DTO.
            {
                Id = p.Id, // PK.
                Title = p.Title, // Title.
                Content = p.Content, // Body.
                CreatedAt = p.CreatedAt, // Timestamp.
                UserId = p.UserId // Author FK.
            })
            .ToListAsync(cancellationToken); // List.
        return (items, total); // Tuple.
    }

    public async Task<(List<PostDto> Items, long TotalCount)> SearchByTitlePagedAsync( // Title contains.
        string titleContains, // Pattern.
        int page, // Page.
        int pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var q = _context.Posts.AsNoTracking().Where(p => p.Title.Contains(titleContains)); // Filter.
        var total = await q.LongCountAsync(cancellationToken); // Count filter.
        var items = await q // Page.
            .OrderByDescending(p => p.CreatedAt) // Sort.
            .ThenBy(p => p.Id) // Tie.
            .Skip((page - 1) * pageSize) // Skip.
            .Take(pageSize) // Take.
            .Select(p => new PostDto // DTO.
            {
                Id = p.Id, // Id.
                Title = p.Title, // Title.
                Content = p.Content, // Content.
                CreatedAt = p.CreatedAt, // Created.
                UserId = p.UserId // User.
            })
            .ToListAsync(cancellationToken); // Execute.
        return (items, total); // Out.
    }

    public Task<PostDto?> GetByIdForReadAsync(Guid id, CancellationToken cancellationToken = default) => // Single DTO read.
        _context.Posts.AsNoTracking() // No tracking.
            .Where(p => p.Id == id) // Filter PK.
            .Select(p => new PostDto // Project.
            {
                Id = p.Id, // Id.
                Title = p.Title, // Title.
                Content = p.Content, // Content.
                CreatedAt = p.CreatedAt, // Created.
                UserId = p.UserId // UserId.
            })
            .FirstOrDefaultAsync(cancellationToken); // Null if missing.

    public async Task<Post?> GetByIdAsync(Guid id) // Tracked or default FirstOrDefault entity.
    {
        return await _context.Posts.FirstOrDefaultAsync(x => x.Id == id); // Entity or null.
    }

    public async Task AddAsync(Post post) // Insert.
    {
        await _context.Posts.AddAsync(post); // Stage.
    }

    public void Update(Post post) // Update.
    {
        _context.Posts.Update(post); // Mark.
    }

    public void Remove(Post post) // Delete.
    {
        _context.Posts.Remove(post); // Stage remove.
    }

    public async Task<bool> ExistsAsync(Guid id) // Exists by id.
    {
        return await _context.Posts.AsNoTracking().AnyAsync(x => x.Id == id); // Any.
    }

    public async Task SaveChangesAsync() // Commit.
    {
        await _context.SaveChangesAsync(); // SaveChanges.
    }
}
