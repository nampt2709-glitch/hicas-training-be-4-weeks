using System.Data.Common;
using CommentAPI.Data;
using CommentAPI.DTOs;
using CommentAPI.Entities;
using CommentAPI.Interfaces;
using CommentAPI.Middleware;
using CommentAPI.Queries;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Repositories;

/// <summary>
/// Truy cập bảng Comments (và bảng liên quan khi kiểm tra Post/User).
/// Mỗi khối <c>await ...Async</c> trên <see cref="IQueryable"/> thường sinh một lệnh SQL riêng (EF không gộp COUNT + SELECT trừ khi dùng kỹ thuật khác).
/// </summary>
public class CommentRepository : ICommentRepository
{
    private readonly AppDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CommentRepository(AppDbContext context, IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Lấy toàn bộ comment: một truy vấn SELECT không theo dõi thay đổi, sắp xếp theo thời gian tạo.
    /// </summary>
    public async Task<List<Comment>> GetAllAsync()
    {
        // AsNoTracking(): không đưa entity vào DbContext → nhẹ hơn cho đọc thuần.
        // OrderBy: SQL ORDER BY CreatedAt — thứ tự ổn định hơn nếu sau này thêm ThenBy(Id).
        return await _context.Comments
            .AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Phân trang toàn bộ comment: hai round-trip SQL — COUNT(*) rồi SELECT một trang có ORDER BY + OFFSET/FETCH.
    /// </summary>
    public async Task<(List<Comment> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Nguồn: DbSet Comments, chỉ đọc (AsNoTracking).
        var q = _context.Comments.AsNoTracking();
        // Truy vấn 1: SELECT COUNT_BIG(*) … — tổng số bản ghi để client biết tổng trang.
        var total = await q.LongCountAsync(cancellationToken);
        // Truy vấn 2: SELECT … ORDER BY PostId, CreatedAt, Id OFFSET @skip ROWS FETCH NEXT @take — một trang dữ liệu.
        var items = await q
            .OrderBy(c => c.PostId)
            .ThenBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    /// <summary>
    /// Phân trang comment thuộc một post: WHERE PostId = …, rồi COUNT và SELECT tương tự <see cref="GetPagedAsync"/>.
    /// </summary>
    public async Task<(List<Comment> Items, long TotalCount)> GetByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Lọc theo khóa ngoại PostId — chỉ mục trong một bài viết.
        var q = _context.Comments.AsNoTracking().Where(c => c.PostId == postId);
        var total = await q.LongCountAsync(cancellationToken);
        var items = await q
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    /// <summary>
    /// Phân trang các comment gốc (không cha) trên toàn hệ thống: WHERE ParentId IS NULL.
    /// </summary>
    public async Task<(List<Comment> Items, long TotalCount)> GetRootCommentsPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var q = _context.Comments.AsNoTracking().Where(c => c.ParentId == null);
        var total = await q.LongCountAsync(cancellationToken);
        var items = await q
            .OrderBy(c => c.PostId)
            .ThenBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    /// <summary>
    /// Phân trang gốc trong một post: PostId cố định và ParentId IS NULL.
    /// </summary>
    public async Task<(List<Comment> Items, long TotalCount)> GetRootsByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var q = _context.Comments.AsNoTracking()
            .Where(c => c.PostId == postId && c.ParentId == null);
        var total = await q.LongCountAsync(cancellationToken);
        var items = await q
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    /// <summary>
    /// Tìm kiếm toàn cục: WHERE Content LIKE N'%term%' (Contains dịch sang SQL chứa chuỗi con).
    /// </summary>
    public async Task<(List<Comment> Items, long TotalCount)> SearchByContentPagedAsync(
        string contentContains,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var q = _context.Comments.AsNoTracking().Where(c => c.Content.Contains(contentContains));
        var total = await q.LongCountAsync(cancellationToken);
        var items = await q
            .OrderBy(c => c.PostId)
            .ThenBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    /// <summary>
    /// Một SELECT trả mọi comment thuộc các post trong tập Id (IN / mở rộng tham số) — phục vụ dựng cây trong service.
    /// </summary>
    public async Task<List<Comment>> GetCommentsForPostsAsync(
        IReadOnlyCollection<Guid> postIds,
        CancellationToken cancellationToken = default)
    {
        if (postIds.Count == 0)
        {
            // Tránh gửi SQL vô nghĩa khi danh sách post rỗng.
            return new List<Comment>();
        }

        return await _context.Comments
            .AsNoTracking()
            .Where(c => postIds.Contains(c.PostId))
            .OrderBy(c => c.PostId)
            .ThenBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Một SELECT lấy toàn bộ comment của một post — thường dùng để dựng cây trong bộ nhớ.
    /// </summary>
    public async Task<List<Comment>> GetByPostIdAsync(Guid postId)
    {
        return await _context.Comments
            .AsNoTracking()
            .Where(x => x.PostId == postId)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Đọc một comment theo Id: SELECT chiếu thẳng sang <see cref="CommentDto"/> (không nạp entity đầy đủ).
    /// </summary>
    public Task<CommentDto?> GetByIdForReadAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Comments.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CommentDto
            {
                Id = c.Id,
                Content = c.Content,
                CreatedAt = c.CreatedAt,
                PostId = c.PostId,
                UserId = c.UserId,
                ParentId = c.ParentId
            })
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>Hai điều kiện AND trên Id và PostId đảm bảo không lộ comment của post khác.</remarks>
    public Task<CommentDto?> GetByIdForReadInPostAsync(Guid postId, Guid commentId, CancellationToken cancellationToken = default) =>
        _context.Comments.AsNoTracking()
            .Where(c => c.Id == commentId && c.PostId == postId)
            .Select(c => new CommentDto
            {
                Id = c.Id,
                Content = c.Content,
                CreatedAt = c.CreatedAt,
                PostId = c.PostId,
                UserId = c.UserId,
                ParentId = c.ParentId
            })
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<(List<Comment> Items, long TotalCount)> SearchByContentInPostPagedAsync(
        Guid postId,
        string contentContains,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Kết hợp lọc post + Contains: giới hạn phạm vi tìm kiếm một bài viết.
        var q = _context.Comments.AsNoTracking().Where(c => c.PostId == postId && c.Content.Contains(contentContains));
        var total = await q.LongCountAsync(cancellationToken);
        var items = await q
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    /// <summary>
    /// Lấy entity tracked (không AsNoTracking) — phục vụ cập nhật/xóa; một SELECT theo khóa chính.
    /// </summary>
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

    /// <summary>SELECT 1 hoặc EXISTS kiểu Any — kiểm tra tồn tại Id.</summary>
    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _context.Comments.AsNoTracking().AnyAsync(x => x.Id == id);
    }

    /// <summary>Kiểm tra post tồn tại — truy vấn tới bảng Posts.</summary>
    public async Task<bool> PostExistsAsync(Guid postId)
    {
        return await _context.Posts.AsNoTracking().AnyAsync(x => x.Id == postId);
    }

    /// <summary>Kiểm tra user — truy vấn bảng Users (Identity).</summary>
    public async Task<bool> UserExistsAsync(Guid userId)
    {
        return await _context.Users.AsNoTracking().AnyAsync(x => x.Id == userId);
    }

    /// <summary>Đảm bảo comment cha cùng post với comment con (cùng PostId).</summary>
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

    /// <summary>
    /// Chạy CTE đệ quy trong file SQL cho một post: <b>không</b> đi qua interceptor EF, nên sau khi ExecuteReader
    /// phải gọi <see cref="CorrelationMiddleware.RecordAdoSqlCommand"/> để header <c>X-Sql-Query-Count</c> đúng.
    /// </summary>
    public async Task<List<CommentFlatDto>> GetTreeRowsByCteAsync(Guid postId)
    {
        // Nạp văn bản SQL từ thư mục Queries — CTE anchor + recursive, tham số @postId.
        var sql = QueryFileReader.ReadSql("CommentTree_ByPost.sql");

        var result = new List<CommentFlatDto>();
        // Cùng connection pool với DbContext; GetDbConnection() không tự mở.
        DbConnection connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;

        if (shouldClose)
        {
            // Mở kết nối vật lý tới SQL Server (có thể không tính là “query” nhưng cần trước ExecuteReader).
            await _context.Database.OpenConnectionAsync();
        }

        try
        {
            // Tạo lệnh ADO.NET thuần — EF không bọc, không tự đếm trong interceptor mặc định.
            using var command = connection.CreateCommand();
            command.CommandText = sql;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@postId";
            parameter.Value = postId;
            command.Parameters.Add(parameter);

            using var reader = await command.ExecuteReaderAsync();
            // Một round-trip thực thi toàn bộ script CTE + trả tập kết quả.
            CorrelationMiddleware.RecordAdoSqlCommand(_httpContextAccessor.HttpContext);

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

    /// <summary>CTE toàn bộ comment mọi post — tương tự <see cref="GetTreeRowsByCteAsync"/> nhưng không tham số post.</summary>
    public async Task<List<CommentFlatDto>> GetTreeRowsByCteAllAsync()
    {
        var sql = QueryFileReader.ReadSql("CommentTree_AllPosts.sql");

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
            CorrelationMiddleware.RecordAdoSqlCommand(_httpContextAccessor.HttpContext);

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

    /// <summary>
    /// Ánh xạ một hàng kết quả CTE sang DTO; thứ tự cột phải khớp SELECT trong file SQL (0..5).
    /// </summary>
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

    /// <inheritdoc />
    /// <remarks>
    /// Truy vấn 1: SELECT comment theo Id (tracked).
    /// Truy vấn bổ sung (lazy): mỗi khi chạm Post.Title, User.UserName, Children.Count có thể phát sinh thêm SELECT — tùy proxy.
    /// </remarks>
    public async Task<CommentLoadingDemoDto?> GetCommentLazyLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Không AsNoTracking: entity tracked để lazy-loading proxy chèn truy vấn khi đọc Post / User / Children.
        var comment = await _context.Comments.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (comment is null)
        {
            return null;
        }

        // Các dòng sau có thể kích hoạt thêm lệnh SQL riêng (navigation chưa nạp).
        var postTitle = comment.Post?.Title;
        var authorUserName = comment.User?.UserName;
        var childrenCount = comment.Children.Count;

        return new CommentLoadingDemoDto
        {
            LoadingStrategy = "lazy",
            CommentId = comment.Id,
            Content = comment.Content,
            PostId = comment.PostId,
            PostTitle = postTitle,
            UserId = comment.UserId,
            AuthorUserName = authorUserName,
            ParentId = comment.ParentId,
            ChildrenCount = childrenCount
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// AsSplitQuery: tách thành nhiều SELECT (thường 1 + số nhánh Include) để tránh tích Cartesian.
    /// Thứ tự thực tế: truy vấn comment theo Id, rồi các truy vấn nạp Post, User, Parent, Children.
    /// </remarks>
    public async Task<CommentLoadingDemoDto?> GetCommentEagerLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var comment = await _context.Comments
            .Include(c => c.Post)
            .Include(c => c.User)
            .Include(c => c.Parent)
            .Include(c => c.Children)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (comment is null)
        {
            return null;
        }

        return new CommentLoadingDemoDto
        {
            LoadingStrategy = "eager",
            CommentId = comment.Id,
            Content = comment.Content,
            PostId = comment.PostId,
            PostTitle = comment.Post?.Title,
            UserId = comment.UserId,
            AuthorUserName = comment.User?.UserName,
            ParentId = comment.ParentId,
            ChildrenCount = comment.Children.Count
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Mỗi lệnh LoadAsync tương ứng một SELECT riêng tới bảng tương ứng (Post, User, Parent, Children).
    /// </remarks>
    public async Task<CommentLoadingDemoDto?> GetCommentExplicitLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var comment = await _context.Comments.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (comment is null)
        {
            return null;
        }

        // LoadAsync: nạp từng navigation một cách tường minh.
        await _context.Entry(comment).Reference(c => c.Post).LoadAsync(cancellationToken);
        await _context.Entry(comment).Reference(c => c.User).LoadAsync(cancellationToken);
        if (comment.ParentId.HasValue)
        {
            await _context.Entry(comment).Reference(c => c.Parent).LoadAsync(cancellationToken);
        }

        await _context.Entry(comment).Collection(c => c.Children).LoadAsync(cancellationToken);

        return new CommentLoadingDemoDto
        {
            LoadingStrategy = "explicit",
            CommentId = comment.Id,
            Content = comment.Content,
            PostId = comment.PostId,
            PostTitle = comment.Post?.Title,
            UserId = comment.UserId,
            AuthorUserName = comment.User?.UserName,
            ParentId = comment.ParentId,
            ChildrenCount = comment.Children.Count
        };
    }

    /// <inheritdoc />
    /// <remarks>Một SELECT duy nhất (hoặc với join con) — Count() trong projection dịch sang subquery/COUNT trên SQL.</remarks>
    public Task<CommentLoadingDemoDto?> GetCommentProjectionDemoAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Comments.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CommentLoadingDemoDto
            {
                LoadingStrategy = "projection",
                CommentId = c.Id,
                Content = c.Content,
                PostId = c.PostId,
                PostTitle = c.Post != null ? c.Post.Title : null,
                UserId = c.UserId,
                AuthorUserName = c.User != null ? c.User.UserName : null,
                ParentId = c.ParentId,
                ChildrenCount = c.Children.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// COUNT + SELECT trang; vòng foreach: mỗi comment có thể thêm nhiều lazy query khi đọc Post, User, Children.
    /// </remarks>
    public async Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsLazyLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var q = _context.Comments;
        var total = await q.LongCountAsync(cancellationToken);
        var rows = await q
            .OrderBy(c => c.PostId)
            .ThenBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var list = new List<CommentLoadingDemoDto>(rows.Count);
        foreach (var comment in rows)
        {
            list.Add(new CommentLoadingDemoDto
            {
                LoadingStrategy = "lazy",
                CommentId = comment.Id,
                Content = comment.Content,
                PostId = comment.PostId,
                PostTitle = comment.Post?.Title,
                UserId = comment.UserId,
                AuthorUserName = comment.User?.UserName,
                ParentId = comment.ParentId,
                ChildrenCount = comment.Children.Count
            });
        }

        return (list, total);
    }

    /// <inheritdoc />
    /// <remarks>COUNT trên base; trang dữ liệu dùng split query — nhiều SELECT nhưng số lượng cố định theo pattern Include.</remarks>
    public async Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsEagerLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _context.Comments.AsNoTracking();
        var total = await baseQuery.LongCountAsync(cancellationToken);
        var rows = await baseQuery
            .Include(c => c.Post)
            .Include(c => c.User)
            .Include(c => c.Parent)
            .Include(c => c.Children)
            .AsSplitQuery()
            .OrderBy(c => c.PostId)
            .ThenBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var list = rows.ConvertAll(comment => new CommentLoadingDemoDto
        {
            LoadingStrategy = "eager",
            CommentId = comment.Id,
            Content = comment.Content,
            PostId = comment.PostId,
            PostTitle = comment.Post?.Title,
            UserId = comment.UserId,
            AuthorUserName = comment.User?.UserName,
            ParentId = comment.ParentId,
            ChildrenCount = comment.Children.Count
        });

        return (list, total);
    }

    /// <inheritdoc />
    /// <remarks>
    /// COUNT + SELECT trang; với mỗi dòng: tối đa 4 LoadAsync — tổng số query tăng tuyến tính theo pageSize.
    /// </remarks>
    public async Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsExplicitLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var q = _context.Comments;
        var total = await q.LongCountAsync(cancellationToken);
        var rows = await q
            .OrderBy(c => c.PostId)
            .ThenBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var list = new List<CommentLoadingDemoDto>(rows.Count);
        foreach (var comment in rows)
        {
            await _context.Entry(comment).Reference(c => c.Post).LoadAsync(cancellationToken);
            await _context.Entry(comment).Reference(c => c.User).LoadAsync(cancellationToken);
            if (comment.ParentId.HasValue)
            {
                await _context.Entry(comment).Reference(c => c.Parent).LoadAsync(cancellationToken);
            }

            await _context.Entry(comment).Collection(c => c.Children).LoadAsync(cancellationToken);

            list.Add(new CommentLoadingDemoDto
            {
                LoadingStrategy = "explicit",
                CommentId = comment.Id,
                Content = comment.Content,
                PostId = comment.PostId,
                PostTitle = comment.Post?.Title,
                UserId = comment.UserId,
                AuthorUserName = comment.User?.UserName,
                ParentId = comment.ParentId,
                ChildrenCount = comment.Children.Count
            });
        }

        return (list, total);
    }

    /// <inheritdoc />
    /// <remarks>Hai truy vấn: COUNT và SELECT trang có projection (join + COUNT con trong SQL).</remarks>
    public async Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsProjectionDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var q = _context.Comments.AsNoTracking();
        var total = await q.LongCountAsync(cancellationToken);
        var items = await q
            .OrderBy(c => c.PostId)
            .ThenBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CommentLoadingDemoDto
            {
                LoadingStrategy = "projection",
                CommentId = c.Id,
                Content = c.Content,
                PostId = c.PostId,
                PostTitle = c.Post != null ? c.Post.Title : null,
                UserId = c.UserId,
                AuthorUserName = c.User != null ? c.User.UserName : null,
                ParentId = c.ParentId,
                ChildrenCount = c.Children.Count()
            })
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    /// <summary>
    /// Đẩy mọi thay đổi tracked xuống DB — có thể là một hoặc nhiều lệnh SQL (INSERT/UPDATE/DELETE) tùy nhà cung cấp và batching.
    /// </summary>
    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}
