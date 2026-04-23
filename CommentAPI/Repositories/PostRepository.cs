using CommentAPI.Entities; 
using CommentAPI.Data; 
using CommentAPI.DTOs; 
using CommentAPI.Interfaces;
using Microsoft.EntityFrameworkCore; 

namespace CommentAPI.Repositories; 

public class PostRepository : IPostRepository // CRUD + read projections.
{
    #region Trường & hàm tạo

    private readonly AppDbContext _context; // Database context.

    public PostRepository(AppDbContext context) // DI.
    {
        _context = context; // Assign.
    }

    #endregion

    #region Private — lọc query

    // Lọc khoảng CreatedAt (inclusive) trên IQueryable Post — dịch sang SQL.
    private static IQueryable<Post> WhereCreatedAtRange(IQueryable<Post> query, DateTime? createdAtFrom, DateTime? createdAtTo)
    {
        if (createdAtFrom is { } f)
            query = query.Where(p => p.CreatedAt >= f);
        if (createdAtTo is { } t)
            query = query.Where(p => p.CreatedAt <= t);
        return query;
    }

    #endregion

    #region GET — PostsController (GetAll, GetById)

    public async Task<(List<PostDto> Items, long TotalCount)> GetPagedAsync( // Paged list as DTO projection.
        int page, // Page (caller normalized in some paths).
        int pageSize, // Size.
        CancellationToken cancellationToken = default, // CT.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null, // Lọc CreatedAt.
        string? titleContains = null, // Filter Title Contains.
        string? contentContains = null) // Filter Content Contains.
    {
        var q = WhereCreatedAtRange(_context.Posts.AsNoTracking(), createdAtFrom, createdAtTo); // Base + khoảng thời gian.
        var t = titleContains?.Trim(); // Chuẩn hóa.
        if (!string.IsNullOrEmpty(t))
            q = q.Where(p => p.Title.Contains(t)); // WHERE Title LIKE %t%.
        var c = contentContains?.Trim(); // Chuẩn hóa nội dung.
        if (!string.IsNullOrEmpty(c))
            q = q.Where(p => p.Content.Contains(c)); // WHERE Content LIKE %c%.
        var total = await q.LongCountAsync(cancellationToken); // Count khớp lọc.
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

    #endregion

    #region Ghi — PostsController (Create, Update, Delete)

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

    #endregion

    #region Bổ trợ — không map route trực tiếp

    public async Task<List<Post>> GetAllAsync() // Full list entities (legacy/helper).
    {
        return await _context.Posts // DbSet.
            .AsNoTracking() // Read-only.
            .OrderByDescending(x => x.CreatedAt) // Newest first.
            .ToListAsync(); // Materialize.
    }

    #endregion
}
