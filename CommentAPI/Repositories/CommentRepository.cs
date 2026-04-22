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

public class CommentRepository : ICommentRepository 
{ 
    // Mở khối lớp CommentRepository.
    private readonly AppDbContext _context; // Ngữ cảnh EF: DbSet và SaveChanges.
    private readonly IHttpContextAccessor _httpContextAccessor; // Truy cập HttpContext để middleware đếm SQL.

    public CommentRepository(AppDbContext context, IHttpContextAccessor httpContextAccessor) // Tiêm dependency qua constructor.
    { // Mở khối constructor.
        _context = context; // Gán DbContext dùng cho mọi truy vấn EF trong lớp này.
        _httpContextAccessor = httpContextAccessor; // Dùng để báo cáo lệnh ADO thô (CTE) vào bộ đếm request.
    } // Kết thúc constructor.

    public async Task<List<Comment>> GetAllAsync() // Đọc toàn bộ comment, không phân trang.
    { // Mở khối GetAllAsync.
        return await _context.Comments // Bắt đầu IQueryable từ DbSet Comments.
            .AsNoTracking() // Truy vấn chỉ đọc, không đưa entity vào ChangeTracker.
            .OrderBy(x => x.CreatedAt) // Sắp xếp tăng dần theo CreatedAt (sinh ORDER BY trong SQL).
            .ToListAsync(); // Biên dịch và thực thi SQL, đọc toàn bộ kết quả vào List<Comment>.
    } // Kết thúc GetAllAsync.

    public async Task<(List<Comment> Items, long TotalCount)> GetPagedAsync( // Phân trang toàn bảng Comments.
        int page, // Số trang (1-based).
        int pageSize, // Số dòng mỗi trang.
        CancellationToken cancellationToken = default) // Hủy bất đồng bộ.
    { // Mở khối GetPagedAsync.
        var q = _context.Comments // Nguồn truy vấn: bảng Comments.
            .AsNoTracking(); // Không track entity cho lần đọc này.
        var total = await q // Dùng lại cùng biểu thức IQueryable đã AsNoTracking.
            .LongCountAsync(cancellationToken); // Sinh COUNT_BIG(*) trên bộ lọc hiện tại; await trả tổng số dòng.
        var items = await q // Lại từ cùng nguồn q để tránh lệch trạng thái.
            .OrderBy(c => c.PostId) // ORDER BY cột PostId (ổn định nhóm theo bài viết).
            .ThenBy(c => c.CreatedAt) // Tiếp theo sắp theo thời gian tạo trong cùng PostId.
            .ThenBy(c => c.Id) // Khóa dư để thứ tự tuyệt đối khi CreatedAt trùng.
            .Skip((page - 1) * pageSize) // Bỏ qua (trang-1)*kích thước dòng (OFFSET trong SQL).
            .Take(pageSize) // Chỉ lấy đúng pageSize dòng tiếp theo (FETCH/LIMIT).
            .ToListAsync(cancellationToken); // Thực thi SELECT và materialize danh sách một trang.
        return (items, total); // Trả tuple: dữ liệu trang + tổng để tính tổng số trang ở API.
    } // Kết thúc GetPagedAsync.

    public async Task<(List<Comment> Items, long TotalCount)> GetByPostIdPagedAsync( // Phân trang comment trong một post.
        Guid postId, // Id bài viết.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetByPostIdPagedAsync.
        var q = _context.Comments // DbSet Comments.
            .AsNoTracking() // Đọc không track.
            .Where(c => c.PostId == postId); // Lọc WHERE PostId = tham số (một bài viết).
        var total = await q.LongCountAsync(cancellationToken); // Đếm tổng comment của post đó.
        var items = await q // Cùng bộ lọc post.
            .OrderBy(c => c.CreatedAt) // Sắp theo thời gian trong post.
            .ThenBy(c => c.Id) // Tie-breaker theo Id.
            .Skip((page - 1) * pageSize) // Phân trang: bỏ qua các dòng trước trang hiện tại.
            .Take(pageSize) // Lấy đúng kích thước trang.
            .ToListAsync(cancellationToken); // Thực thi SQL và trả List.
        return (items, total); // Tuple trang và tổng trong post.
    } // Kết thúc GetByPostIdPagedAsync.

    public async Task<(List<Comment> Items, long TotalCount)> GetRootCommentsPagedAsync( // Phân trang comment gốc toàn hệ thống.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetRootCommentsPagedAsync.
        var q = _context.Comments // DbSet.
            .AsNoTracking() // Không track.
            .Where(c => c.ParentId == null); // Chỉ comment gốc (không có cha).
        var total = await q.LongCountAsync(cancellationToken); // Đếm số gốc toàn hệ thống.
        var items = await q // Tiếp tục từ cùng IQueryable q.
            .OrderBy(c => c.PostId) // Gốc sắp theo bài viết.
            .ThenBy(c => c.CreatedAt) // Rồi theo thời gian.
            .ThenBy(c => c.Id) // Rồi Id.
            .Skip((page - 1) * pageSize) // OFFSET theo trang.
            .Take(pageSize) // Giới hạn số dòng.
            .ToListAsync(cancellationToken); // Thực thi và materialize.
        return (items, total); // Trả trang gốc và tổng số gốc.
    } // Kết thúc GetRootCommentsPagedAsync.

    public async Task<(List<Comment> Items, long TotalCount)> GetRootsByPostIdPagedAsync( // Gốc trong một post, phân trang.
        Guid postId, // Bài viết.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetRootsByPostIdPagedAsync.
        var q = _context.Comments // DbSet.
            .AsNoTracking() // Không track.
            .Where(c => c.PostId == postId && c.ParentId == null); // Gốc trong một post cụ thể.
        var total = await q.LongCountAsync(cancellationToken); // Đếm số gốc trong post.
        var items = await q // Tiếp từ q.
            .OrderBy(c => c.CreatedAt) // Thứ tự gốc theo thời gian.
            .ThenBy(c => c.Id) // Tie-breaker.
            .Skip((page - 1) * pageSize) // Phân trang.
            .Take(pageSize) // Kích thước trang.
            .ToListAsync(cancellationToken); // Thực thi SELECT.
        return (items, total); // Trả trang gốc và tổng gốc trong post.
    } // Kết thúc GetRootsByPostIdPagedAsync.

    public async Task<(List<Comment> Items, long TotalCount)> SearchByContentPagedAsync( // Tìm theo nội dung, toàn hệ thống.
        string contentContains, // Chuỗi con cần chứa trong Content.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối SearchByContentPagedAsync.
        var q = _context.Comments // DbSet.
            .AsNoTracking() // Không track.
            .Where(c => c.Content.Contains(contentContains)); // EF dịch Contains thành điều kiện LIKE/%chuỗi% trên SQL Server.
        var total = await q.LongCountAsync(cancellationToken); // Đếm số khớp toàn cục.
        var items = await q // Tiếp từ q.
            .OrderBy(c => c.PostId) // Sắp kết quả tìm kiếm theo post.
            .ThenBy(c => c.CreatedAt) // Rồi thời gian.
            .ThenBy(c => c.Id) // Rồi Id.
            .Skip((page - 1) * pageSize) // Phân trang.
            .Take(pageSize) // Giới hạn trang.
            .ToListAsync(cancellationToken); // Materialize.
        return (items, total); // Trả trang khớp và tổng khớp.
    } // Kết thúc SearchByContentPagedAsync.

    public async Task<List<Comment>> GetCommentsForPostsAsync( // Một SELECT cho nhiều post (IN PostId).
        IReadOnlyCollection<Guid> postIds, // Tập Id bài viết.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetCommentsForPostsAsync.
        if (postIds.Count == 0) // Không có post nào cần nạp.
        { // Mở khối.
            return new List<Comment>(); // Trả list rỗng, không tạo câu SQL IN rỗng.
        } // Kết thúc nhánh rỗng.

        return await _context.Comments // DbSet.
            .AsNoTracking() // Không track.
            .Where(c => postIds.Contains(c.PostId)) // WHERE PostId IN (...danh sách guid...).
            .OrderBy(c => c.PostId) // Sắp theo post để nhóm tự nhiên khi đọc.
            .ThenBy(c => c.CreatedAt) // Trong post theo thời gian.
            .ThenBy(c => c.Id) // Tie-breaker.
            .ToListAsync(cancellationToken); // Một SELECT trả toàn bộ comment thuộc các post đó.
    } // Kết thúc GetCommentsForPostsAsync.

    public async Task<List<Comment>> GetByPostIdAsync(Guid postId) // Toàn comment một post, không track.
    { // Mở khối GetByPostIdAsync.
        return await _context.Comments // DbSet.
            .AsNoTracking() // Không track.
            .Where(x => x.PostId == postId) // Một post.
            .OrderBy(x => x.CreatedAt) // Thứ tự thời gian.
            .ThenBy(x => x.Id) // Tie-breaker.
            .ToListAsync(); // Thực thi SELECT toàn bộ comment của post (có thể lớn).
    } // Kết thúc GetByPostIdAsync.

    // Nạp mọi comment thuộc post với trạng thái track — phục vụ cập nhật PostId cùng lúc cho cả tập con cây.
    public async Task<List<Comment>> GetByPostIdTrackedAsync( // Truy vấn tracked để SaveChanges cập nhật hàng loạt.
        Guid postId, // Bài viết.
        CancellationToken cancellationToken = default) => // Biểu thức thân phương thức bất đồng bộ.
        await _context.Comments // DbSet (tracked mặc định khi không AsNoTracking).
            .Where(x => x.PostId == postId) // Lọc theo post.
            .OrderBy(x => x.CreatedAt) // Thứ tự ổn định.
            .ThenBy(x => x.Id) // Tie-breaker.
            .ToListAsync(cancellationToken); // Materialize danh sách tracked.

    public Task<CommentDto?> GetByIdForReadAsync(Guid id, CancellationToken cancellationToken = default) => // Chiếu một comment sang DTO.
        _context.Comments // DbSet.
            .AsNoTracking() // Không track.
            .Where(c => c.Id == id) // Lọc một khóa chính.
            .Select(c => new CommentDto // Chiếu thẳng sang DTO trong SQL (không SELECT * entity đầy đủ).
            { // Mở khối initializer.
                Id = c.Id, // Cột Id vào DTO.
                Content = c.Content, // Nội dung.
                CreatedAt = c.CreatedAt, // Thời gian tạo.
                PostId = c.PostId, // Khóa post.
                UserId = c.UserId, // Khóa user.
                ParentId = c.ParentId // Cha (nullable).
            }) // Kết thúc projection object.
            .FirstOrDefaultAsync(cancellationToken); // SELECT TOP 1 ... hoặc tương đương; null nếu không có.

    public Task<CommentDto?> GetByIdForReadInPostAsync(Guid postId, Guid commentId, CancellationToken cancellationToken = default) => // Đọc trong phạm vi post.
        _context.Comments // DbSet.
            .AsNoTracking() // Không track.
            .Where(c => c.Id == commentId && c.PostId == postId) // AND hai điều kiện: đúng comment và đúng post.
            .Select(c => new CommentDto // Projection.
            { // Mở khối initializer.
                Id = c.Id, // Id.
                Content = c.Content, // Nội dung.
                CreatedAt = c.CreatedAt, // Thời gian.
                PostId = c.PostId, // Post.
                UserId = c.UserId, // User.
                ParentId = c.ParentId // Cha.
            }) // Kết thúc anonymous projection.
            .FirstOrDefaultAsync(cancellationToken); // Một dòng hoặc null.

    public async Task<(List<Comment> Items, long TotalCount)> SearchByContentInPostPagedAsync( // Tìm nội dung trong một post.
        Guid postId, // Bài viết.
        string contentContains, // Chuỗi con.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối SearchByContentInPostPagedAsync.
        var q = _context.Comments // DbSet.
            .AsNoTracking() // Không track.
            .Where(c => c.PostId == postId && c.Content.Contains(contentContains)); // Cùng lúc lọc post và LIKE nội dung.
        var total = await q.LongCountAsync(cancellationToken); // Đếm khớp trong post.
        var items = await q // Tiếp từ q.
            .OrderBy(c => c.CreatedAt) // Sắp theo thời gian trong post.
            .ThenBy(c => c.Id) // Tie-breaker.
            .Skip((page - 1) * pageSize) // OFFSET.
            .Take(pageSize) // FETCH.
            .ToListAsync(cancellationToken); // Materialize trang.
        return (items, total); // Trả trang và tổng trong post.
    } // Kết thúc SearchByContentInPostPagedAsync.

    public async Task<Comment?> GetByIdAsync(Guid id) // Nạp entity có thể tracked (sửa/xóa).
    { // Mở khối GetByIdAsync.
        return await _context.Comments // DbSet (mặc định có thể track entity).
            .FirstOrDefaultAsync(x => x.Id == id); // Sinh SELECT TOP 1 ... WHERE Id = ...; entity có thể tracked để sửa/xóa.
    } // Kết thúc GetByIdAsync.

    public async Task AddAsync(Comment comment) // Đánh dấu thêm entity.
    { // Mở khối AddAsync.
        await _context.Comments.AddAsync(comment); // Đánh dấu thêm vào DbContext; SQL khi SaveChanges.
    } // Kết thúc AddAsync.

    public void Update(Comment comment) // Đánh dấu entity đã chỉnh sửa.
    { // Mở khối Update.
        _context.Comments.Update(comment); // Đánh dấu entity đã sửa; SQL khi SaveChanges.
    } // Kết thúc Update.

    public void Remove(Comment comment) // Đánh dấu xóa entity.
    { // Mở khối Remove.
        _context.Comments.Remove(comment); // Đánh dấu xóa; SQL khi SaveChanges.
    } // Kết thúc Remove.

    public async Task<bool> ExistsAsync(Guid id) // Kiểm tra tồn tại theo khóa chính.
    { // Mở khối ExistsAsync.
        return await _context.Comments // DbSet.
            .AsNoTracking() // Không cần track cho kiểm tra tồn tại.
            .AnyAsync(x => x.Id == id); // Sinh EXISTS/WHERE giới hạn 1 bit kết quả.
    } // Kết thúc ExistsAsync.

    public async Task<bool> PostExistsAsync(Guid postId) // Kiểm tra bài viết tồn tại.
    { // Mở khối PostExistsAsync.
        return await _context.Posts // DbSet Posts.
            .AsNoTracking() // Chỉ đọc.
            .AnyAsync(x => x.Id == postId); // Kiểm tra một Id post tồn tại.
    } // Kết thúc PostExistsAsync.

    public async Task<bool> UserExistsAsync(Guid userId) // Kiểm tra người dùng tồn tại.
    { // Mở khối UserExistsAsync.
        return await _context.Users // DbSet Users (Identity).
            .AsNoTracking() // Chỉ đọc.
            .AnyAsync(x => x.Id == userId); // Kiểm tra user tồn tại.
    } // Kết thúc UserExistsAsync.

    public async Task<bool> ParentExistsAsync(Guid parentId, Guid postId) // Cha phải cùng post.
    { // Mở khối ParentExistsAsync.
        return await _context.Comments // DbSet Comments.
            .AsNoTracking() // Chỉ đọc.
            .AnyAsync(x => x.Id == parentId && x.PostId == postId); // Cha phải cùng post với con.
    } // Kết thúc ParentExistsAsync.

    public async Task<List<CommentFlatDto>> GetTreeRowsByCteAsync(Guid postId) // CTE một post qua ADO.
    { // Mở khối GetTreeRowsByCteAsync.
        var sql = QueryFileReader.ReadSql("CommentTree_ByPost.sql"); // Đọc file SQL chứa CTE + tham số @postId.

        var result = new List<CommentFlatDto>(); // Bộ chứa các hàng đọc từ reader.
        DbConnection connection = _context.Database.GetDbConnection(); // Lấy connection đã gắn với DbContext (chưa mở tự động).
        var shouldClose = connection.State != System.Data.ConnectionState.Open; // Ghi nhớ có cần đóng connection sau không.

        if (shouldClose) // Connection đang đóng.
        { // Mở khối.
            await _context.Database.OpenConnectionAsync(); // Mở connection để chạy lệnh ADO.
        } // Kết thúc if mở connection.

        try // Khối thử: thực thi lệnh và đọc reader.
        { // Mở khối try.
            using var command = connection.CreateCommand(); // Tạo DbCommand thuần (không qua EF command interceptor mặc định).
            command.CommandText = sql; // Gán văn bản CTE.

            var parameter = command.CreateParameter(); // Tạo tham số ADO.
            parameter.ParameterName = "@postId"; // Tên khớp trong file SQL.
            parameter.Value = postId; // Giá trị Guid.
            command.Parameters.Add(parameter); // Gắn tham số vào lệnh.

            using var reader = await command.ExecuteReaderAsync(); // Gửi một round-trip SQL, trả DbDataReader lướt từng hàng.
            RequestPerformanceMiddleware.RecordAdoSqlCommand(_httpContextAccessor.HttpContext); // Bù đếm vì lệnh không đi qua EF interceptor.

            while (await reader.ReadAsync()) // Đọc từng hàng còn lại trong tập kết quả.
            { // Mở khối vòng lặp.
                result.Add(MapCommentFlatRow(reader)); // Ánh xạ hàng hiện tại sang DTO.
            } // Kết thúc while.

            return result; // Trả toàn bộ hàng CTE đã đọc.
        } // Kết thúc try.
        finally // Luôn chạy: đóng connection nếu method đã mở.
        { // Mở khối finally.
            if (shouldClose) // Nếu chính method này đã mở connection.
            { // Mở khối.
                await _context.Database.CloseConnectionAsync(); // Đóng để không rò connection.
            } // Kết thúc if đóng.
        } // Kết thúc finally.
    } // Kết thúc GetTreeRowsByCteAsync.

    public async Task<List<CommentFlatDto>> GetTreeRowsByCteAllAsync() // CTE toàn bộ post, không tham số post.
    { // Mở khối GetTreeRowsByCteAllAsync.
        var sql = QueryFileReader.ReadSql("CommentTree_AllPosts.sql"); // SQL CTE toàn bộ post (không tham số post).

        var result = new List<CommentFlatDto>(); // Danh sách kết quả.
        DbConnection connection = _context.Database.GetDbConnection(); // Connection của context.
        var shouldClose = connection.State != System.Data.ConnectionState.Open; // Cờ đóng sau cùng.

        if (shouldClose) // Cần mở connection.
        { // Mở khối.
            await _context.Database.OpenConnectionAsync(); // Mở nếu cần.
        } // Kết thúc if.

        try // Thực thi ADO.
        { // Mở khối try.
            using var command = connection.CreateCommand(); // Command ADO.
            command.CommandText = sql; // Script CTE toàn cục.

            using var reader = await command.ExecuteReaderAsync(); // Thực thi và nhận reader.
            RequestPerformanceMiddleware.RecordAdoSqlCommand(_httpContextAccessor.HttpContext); // Đếm một lệnh SQL thô.

            while (await reader.ReadAsync()) // Lặp từng hàng.
            { // Mở khối.
                result.Add(MapCommentFlatRow(reader)); // Map sang DTO.
            } // Kết thúc while.

            return result; // Danh sách đầy đủ hàng CTE.
        } // Kết thúc try.
        finally // Dọn connection.
        { // Mở khối finally.
            if (shouldClose) // Method đã mở.
            { // Mở khối.
                await _context.Database.CloseConnectionAsync(); // Đóng kết nối.
            } // Kết thúc if.
        } // Kết thúc finally.
    } // Kết thúc GetTreeRowsByCteAllAsync.

    private static CommentFlatDto MapCommentFlatRow(DbDataReader reader) // Ánh xạ một hàng reader → DTO (thứ tự cột cố định).
    { // Mở khối MapCommentFlatRow.
        return new CommentFlatDto // Tạo DTO từ một hàng reader (thứ tự cột khớp CommentTree_*.sql).
        { // Mở initializer.
            Id = reader.GetGuid(0), // Cột 0: Id.
            Content = reader.GetString(1), // Cột 1: Content.
            CreatedAt = reader.GetDateTime(2), // Cột 2: CreatedAt.
            ParentId = reader.IsDBNull(3) ? null : reader.GetGuid(3), // Cột 3: ParentId nullable.
            PostId = reader.GetGuid(4), // Cột 4: PostId.
            UserId = reader.GetGuid(5), // Cột 5: UserId.
            Level = reader.GetInt32(6) // Cột 6: Level.
        }; // Kết thúc khởi tạo DTO.
    } // Kết thúc MapCommentFlatRow.

    public async Task<CommentLoadingDemoDto?> GetCommentLazyLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default) // Demo lazy.
    { // Mở khối.
        var comment = await _context.Comments // DbSet không AsNoTracking → entity tracked.
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken); // Một SELECT nạp comment; navigation chưa nạp.
        if (comment is null) // Không tìm thấy Id.
        { // Mở khối.
            return null; // Báo không có dữ liệu demo.
        } // Kết thúc if.

        var postTitle = comment.Post?.Title; // Truy cập Post có thể phát sinh thêm SELECT lazy (proxy).
        var authorUserName = comment.User?.UserName; // Truy cập User có thể phát sinh thêm SELECT.
        var childrenCount = comment.Children.Count; // Đếm con có thể phát sinh SELECT nạp collection.

        return new CommentLoadingDemoDto // Gói kết quả demo (không phải truy vấn SQL).
        { // Mở initializer.
            LoadingStrategy = "lazy", // Nhãn chiến lược nạp.
            CommentId = comment.Id, // Id comment.
            Content = comment.Content, // Nội dung.
            PostId = comment.PostId, // Post.
            PostTitle = postTitle, // Tiêu đề (có thể lazy).
            UserId = comment.UserId, // User.
            AuthorUserName = authorUserName, // Tên đăng nhập (có thể lazy).
            ParentId = comment.ParentId, // Cha.
            ChildrenCount = childrenCount // Số con (có thể lazy).
        }; // Kết thúc DTO.
    } // Kết thúc GetCommentLazyLoadingDemoAsync.

    public async Task<CommentLoadingDemoDto?> GetCommentEagerLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default) // Demo eager.
    { // Mở khối.
        var comment = await _context.Comments // DbSet.
            .Include(c => c.Post) // Yêu cầu JOIN/nạp Post trong truy vấn (hoặc truy vấn split).
            .Include(c => c.User) // Nạp User.
            .Include(c => c.Parent) // Nạp Parent.
            .Include(c => c.Children) // Nạp tập con.
            .AsSplitQuery() // Tách thành nhiều SELECT để tránh nhân bản hàng.
            .AsNoTracking() // Không track sau khi đọc.
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken); // Thực thi chuỗi Include + điều kiện Id.
        if (comment is null) // Không có bản ghi.
        { // Mở khối.
            return null; // Null cho caller.
        } // Kết thúc if.

        return new CommentLoadingDemoDto // DTO kết quả.
        { // Mở initializer.
            LoadingStrategy = "eager", // Chiến lược nạp sớm.
            CommentId = comment.Id, // Id.
            Content = comment.Content, // Nội dung.
            PostId = comment.PostId, // Post.
            PostTitle = comment.Post?.Title, // Đã nạp sẵn, không lazy thêm.
            UserId = comment.UserId, // User.
            AuthorUserName = comment.User?.UserName, // Đã nạp.
            ParentId = comment.ParentId, // Cha.
            ChildrenCount = comment.Children.Count // Collection đã nạp, đếm trong bộ nhớ.
        }; // Kết thúc DTO.
    } // Kết thúc GetCommentEagerLoadingDemoAsync.

    public async Task<CommentLoadingDemoDto?> GetCommentExplicitLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default) // Demo explicit.
    { // Mở khối.
        var comment = await _context.Comments // DbSet tracked.
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken); // SELECT đầu tiên chỉ comment.
        if (comment is null) // Không tìm thấy.
        { // Mở khối.
            return null; // Null.
        } // Kết thúc if.

        await _context.Entry(comment).Reference(c => c.Post).LoadAsync(cancellationToken); // SELECT nạp Post theo FK.
        await _context.Entry(comment).Reference(c => c.User).LoadAsync(cancellationToken); // SELECT nạp User.
        if (comment.ParentId.HasValue) // Có cha mới cần nạp Parent.
        { // Mở khối.
            await _context.Entry(comment).Reference(c => c.Parent).LoadAsync(cancellationToken); // SELECT nạp Parent.
        } // Kết thúc if Parent.

        await _context.Entry(comment).Collection(c => c.Children).LoadAsync(cancellationToken); // SELECT nạp danh sách con.

        return new CommentLoadingDemoDto // DTO sau khi nạp rõ ràng.
        { // Mở initializer.
            LoadingStrategy = "explicit", // Chiến lược nạp tường minh.
            CommentId = comment.Id, // Id.
            Content = comment.Content, // Nội dung.
            PostId = comment.PostId, // Post.
            PostTitle = comment.Post?.Title, // Đã LoadAsync.
            UserId = comment.UserId, // User.
            AuthorUserName = comment.User?.UserName, // Đã LoadAsync.
            ParentId = comment.ParentId, // Cha.
            ChildrenCount = comment.Children.Count // Đã nạp collection.
        }; // Kết thúc DTO.
    } // Kết thúc GetCommentExplicitLoadingDemoAsync.

    public Task<CommentLoadingDemoDto?> GetCommentProjectionDemoAsync(Guid id, CancellationToken cancellationToken = default) => // Một query chiếu DTO.
        _context.Comments // DbSet.
            .AsNoTracking() // Không track.
            .Where(c => c.Id == id) // Một Id.
            .Select(c => new CommentLoadingDemoDto // Toàn bộ trong một biểu thức SQL.
            { // Mở initializer.
                LoadingStrategy = "projection", // Nhãn projection.
                CommentId = c.Id, // Id.
                Content = c.Content, // Nội dung.
                PostId = c.PostId, // Post.
                PostTitle = c.Post != null ? c.Post.Title : null, // LEFT JOIN hoặc subquery Post.Title.
                UserId = c.UserId, // User.
                AuthorUserName = c.User != null ? c.User.UserName : null, // LEFT JOIN User.
                ParentId = c.ParentId, // Cha.
                ChildrenCount = c.Children.Count() // COUNT(*) con trong SQL (IQueryable Count).
            }) // Kết thúc projection.
            .FirstOrDefaultAsync(cancellationToken); // Thực thi một SELECT chiếu DTO.

    public async Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsLazyLoadingDemoPagedAsync( // Demo phân trang lazy.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối.
        var q = _context.Comments // Tracked set (không AsNoTracking) để lazy sau này.
            ; // Kết thúc gán q.
        var total = await q // Đếm trên toàn bộ Comments tracked queryable.
            .LongCountAsync(cancellationToken); // COUNT_BIG toàn bảng (hoặc không lọc).
        var rows = await q // Lại từ q.
            .OrderBy(c => c.PostId) // ORDER BY PostId.
            .ThenBy(c => c.CreatedAt) // Then CreatedAt.
            .ThenBy(c => c.Id) // Then Id.
            .Skip((page - 1) * pageSize) // OFFSET.
            .Take(pageSize) // FETCH.
            .ToListAsync(cancellationToken); // SELECT một trang entity tracked.

        var list = new List<CommentLoadingDemoDto>(rows.Count); // Cấp phát sẵn dung lượng.
        foreach (var comment in rows) // Duyệt từng comment đã nạp.
        { // Mở khối foreach.
            list.Add(new CommentLoadingDemoDto // Mỗi vòng: đọc navigation có thể bắn thêm SQL lazy.
            { // Mở initializer.
                LoadingStrategy = "lazy", // Nhãn.
                CommentId = comment.Id, // Id.
                Content = comment.Content, // Nội dung.
                PostId = comment.PostId, // Post.
                PostTitle = comment.Post?.Title, // Có thể lazy load Post.
                UserId = comment.UserId, // User.
                AuthorUserName = comment.User?.UserName, // Có thể lazy User.
                ParentId = comment.ParentId, // Cha.
                ChildrenCount = comment.Children.Count // Có thể lazy Children.
            }); // Kết thúc Add.
        } // Kết thúc foreach.

        return (list, total); // Tuple kết quả và tổng.
    } // Kết thúc GetCommentsLazyLoadingDemoPagedAsync.

    public async Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsEagerLoadingDemoPagedAsync( // Phân trang eager.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối.
        var baseQuery = _context.Comments // DbSet.
            .AsNoTracking(); // Không track.
        var total = await baseQuery // Đếm trước khi Include để không đổi semantics lọc.
            .LongCountAsync(cancellationToken); // COUNT.
        var rows = await baseQuery // Cùng base không Include cho count, nhưng đây là query mới từ cùng baseQuery.
            .Include(c => c.Post) // Nạp Post.
            .Include(c => c.User) // Nạp User.
            .Include(c => c.Parent) // Nạp Parent.
            .Include(c => c.Children) // Nạp Children.
            .AsSplitQuery() // Tách query.
            .OrderBy(c => c.PostId) // Sắp trang.
            .ThenBy(c => c.CreatedAt) // Thời gian.
            .ThenBy(c => c.Id) // Id.
            .Skip((page - 1) * pageSize) // OFFSET.
            .Take(pageSize) // FETCH.
            .ToListAsync(cancellationToken); // Thực thi chuỗi split + phân trang.

        var list = rows.ConvertAll(comment => new CommentLoadingDemoDto // Ánh xạ trong RAM, không SQL.
        { // Mở initializer.
            LoadingStrategy = "eager", // Nhãn eager.
            CommentId = comment.Id, // Id.
            Content = comment.Content, // Nội dung.
            PostId = comment.PostId, // Post.
            PostTitle = comment.Post?.Title, // Đã Include.
            UserId = comment.UserId, // User.
            AuthorUserName = comment.User?.UserName, // Đã Include.
            ParentId = comment.ParentId, // Cha.
            ChildrenCount = comment.Children.Count // Đã nạp collection.
        }); // Kết thúc ConvertAll.

        return (list, total); // Trả danh sách và tổng.
    } // Kết thúc GetCommentsEagerLoadingDemoPagedAsync.

    public async Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsExplicitLoadingDemoPagedAsync( // Phân trang explicit.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối.
        var q = _context.Comments // Tracked.
            ; // Gán q.
        var total = await q.LongCountAsync(cancellationToken); // COUNT.
        var rows = await q // Tiếp từ q.
            .OrderBy(c => c.PostId) // Sắp post.
            .ThenBy(c => c.CreatedAt) // Thời gian.
            .ThenBy(c => c.Id) // Id.
            .Skip((page - 1) * pageSize) // OFFSET.
            .Take(pageSize) // FETCH.
            .ToListAsync(cancellationToken); // SELECT trang tracked.

        var list = new List<CommentLoadingDemoDto>(rows.Count); // Danh sách đích.
        foreach (var comment in rows) // Từng dòng trang.
        { // Mở khối.
            await _context.Entry(comment).Reference(c => c.Post).LoadAsync(cancellationToken); // SQL: nạp Post.
            await _context.Entry(comment).Reference(c => c.User).LoadAsync(cancellationToken); // SQL: nạp User.
            if (comment.ParentId.HasValue) // Có cha.
            { // Mở khối.
                await _context.Entry(comment).Reference(c => c.Parent).LoadAsync(cancellationToken); // SQL: nạp Parent nếu có.
            } // Kết thúc if.

            await _context.Entry(comment).Collection(c => c.Children).LoadAsync(cancellationToken); // SQL: nạp Children.

            list.Add(new CommentLoadingDemoDto // Thêm DTO sau khi nạp.
            { // Mở initializer.
                LoadingStrategy = "explicit", // Nhãn.
                CommentId = comment.Id, // Id.
                Content = comment.Content, // Nội dung.
                PostId = comment.PostId, // Post.
                PostTitle = comment.Post?.Title, // Đã Load.
                UserId = comment.UserId, // User.
                AuthorUserName = comment.User?.UserName, // Đã Load.
                ParentId = comment.ParentId, // Cha.
                ChildrenCount = comment.Children.Count // Đã Load collection.
            }); // Kết thúc Add.
        } // Kết thúc foreach.

        return (list, total); // Trả tuple.
    } // Kết thúc GetCommentsExplicitLoadingDemoPagedAsync.

    public async Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsProjectionDemoPagedAsync( // Phân trang projection.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối.
        var q = _context.Comments // DbSet.
            .AsNoTracking(); // Không track.
        var total = await q.LongCountAsync(cancellationToken); // COUNT.
        var items = await q // Tiếp từ q.
            .OrderBy(c => c.PostId) // Sắp.
            .ThenBy(c => c.CreatedAt) // Thời gian.
            .ThenBy(c => c.Id) // Id.
            .Skip((page - 1) * pageSize) // OFFSET.
            .Take(pageSize) // FETCH.
            .Select(c => new CommentLoadingDemoDto // Toàn bộ phân trang + projection trên server.
            { // Mở initializer.
                LoadingStrategy = "projection", // Nhãn.
                CommentId = c.Id, // Id.
                Content = c.Content, // Nội dung.
                PostId = c.PostId, // Post.
                PostTitle = c.Post != null ? c.Post.Title : null, // Title trong SQL.
                UserId = c.UserId, // User.
                AuthorUserName = c.User != null ? c.User.UserName : null, // UserName trong SQL.
                ParentId = c.ParentId, // Cha.
                ChildrenCount = c.Children.Count() // Đếm con trong SQL.
            }) // Kết thúc Select projection.
            .ToListAsync(cancellationToken); // Thực thi SELECT trả DTO trực tiếp.

        return (items, total); // Trả trang DTO và tổng.
    } // Kết thúc GetCommentsProjectionDemoPagedAsync.

    public async Task SaveChangesAsync() // Flush thay đổi tracked xuống database.
    { // Mở khối.
        await _context.SaveChangesAsync(); // Gửi mọi thay đổi tracked xuống DB (INSERT/UPDATE/DELETE tùy trạng thái).
    } // Kết thúc SaveChangesAsync.
} // Kết thúc lớp CommentRepository.
