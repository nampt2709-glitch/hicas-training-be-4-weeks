using System.Data.Common;
using CommentAPI.Data;
using CommentAPI.DTOs;
using CommentAPI.Entities;
using CommentAPI.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Repositories;

public class CommentRepository : ICommentRepository
{
    private readonly AppDbContext _context;

    public CommentRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<Comment>> GetAllAsync()
    {
        return await _context.Comments
            .AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Comment>> GetByPostIdAsync(Guid postId)
    {
        return await _context.Comments
            .AsNoTracking()
            .Where(x => x.PostId == postId)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToListAsync();
    }

    public async Task<Comment?> GetByIdAsync(Guid id)
    {
        return await _context.Comments.FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task AddAsync(Comment comment)
    {
        await _context.Comments.AddAsync(comment);
    }

    public void Update(Comment comment)
    {
        _context.Comments.Update(comment);
    }

    public void Remove(Comment comment)
    {
        _context.Comments.Remove(comment);
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _context.Comments.AsNoTracking().AnyAsync(x => x.Id == id);
    }

    public async Task<bool> PostExistsAsync(Guid postId)
    {
        return await _context.Posts.AsNoTracking().AnyAsync(x => x.Id == postId);
    }

    public async Task<bool> UserExistsAsync(Guid userId)
    {
        return await _context.Users.AsNoTracking().AnyAsync(x => x.Id == userId);
    }

    public async Task<bool> ParentExistsAsync(Guid parentId, Guid postId)
    {
        return await _context.Comments
            .AsNoTracking()
            .AnyAsync(x => x.Id == parentId && x.PostId == postId);
    }

    /*
     * Recursive CTE for one post (GetTreeRowsByCteAsync):
     * - Anchor: roots for that post.
     * - Recursive step: join child to parent on Id AND same PostId (no cross-post edges).
     * - Returns flat rows with Level; PostId included for a consistent CommentFlatDto shape.
     * - MAXRECURSION 256 caps runaway cycles.
     */

    public async Task<List<CommentFlatDto>> GetTreeRowsByCteAsync(Guid postId)
    {
        // Per-post CTE: same PostId on recursive join; SELECT includes PostId for DTO mapping.
        const string sql = @"
WITH CommentTree AS (
    SELECT
        c.Id,
        c.Content,
        c.CreatedAt,
        c.ParentId,
        c.PostId,
        0 AS Level
    FROM Comments c
    WHERE c.PostId = @postId
      AND c.ParentId IS NULL

    UNION ALL

    SELECT
        c.Id,
        c.Content,
        c.CreatedAt,
        c.ParentId,
        c.PostId,
        ct.Level + 1
    FROM Comments c
    INNER JOIN CommentTree ct
        ON c.ParentId = ct.Id
       AND c.PostId = ct.PostId
    WHERE c.PostId = @postId
)
SELECT
    Id,
    Content,
    CreatedAt,
    ParentId,
    PostId,
    Level
FROM CommentTree
ORDER BY Level, CreatedAt, Id
OPTION (MAXRECURSION 256);
";

        var result = new List<CommentFlatDto>();
        DbConnection connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;

        if (shouldClose)
        {
            await _context.Database.OpenConnectionAsync();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@postId";
            parameter.Value = postId;
            command.Parameters.Add(parameter);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(MapCommentFlatRow(reader));
            }

            return result;
        }
        finally
        {
            if (shouldClose)
            {
                await _context.Database.CloseConnectionAsync();
            }
        }
    }

    public async Task<List<CommentFlatDto>> GetTreeRowsByCteAllAsync()
    {
        // Global CTE: all roots, then children matched on ParentId and same PostId.
        const string sql = @"
WITH CommentTree AS (
    SELECT
        c.Id,
        c.Content,
        c.CreatedAt,
        c.ParentId,
        c.PostId,
        0 AS Level
    FROM Comments c
    WHERE c.ParentId IS NULL

    UNION ALL

    SELECT
        c.Id,
        c.Content,
        c.CreatedAt,
        c.ParentId,
        c.PostId,
        ct.Level + 1
    FROM Comments c
    INNER JOIN CommentTree ct
        ON c.ParentId = ct.Id
       AND c.PostId = ct.PostId
)
SELECT
    Id,
    Content,
    CreatedAt,
    ParentId,
    PostId,
    Level
FROM CommentTree
ORDER BY PostId, Level, CreatedAt, Id
OPTION (MAXRECURSION 256);
";

        var result = new List<CommentFlatDto>();
        DbConnection connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;

        if (shouldClose)
        {
            await _context.Database.OpenConnectionAsync();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(MapCommentFlatRow(reader));
            }

            return result;
        }
        finally
        {
            if (shouldClose)
            {
                await _context.Database.CloseConnectionAsync();
            }
        }
    }

    /// <summary>Maps one CTE result row (fixed column order) to <see cref="CommentFlatDto"/>.</summary>
    private static CommentFlatDto MapCommentFlatRow(DbDataReader reader)
    {
        return new CommentFlatDto
        {
            Id = reader.GetGuid(0),
            Content = reader.GetString(1),
            CreatedAt = reader.GetDateTime(2),
            ParentId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
            PostId = reader.GetGuid(4),
            Level = reader.GetInt32(5)
        };
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}
