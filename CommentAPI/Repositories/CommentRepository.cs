using System.Linq.Expressions; // Expression trees: dựng predicate động cho lọc CTE trên DTO.
using CommentAPI.Data; // DbContext ứng dụng.
using CommentAPI.DTOs; // DTO trả về / projection.
using CommentAPI.Entities; // Entity Comment và liên quan.
using CommentAPI.Interfaces; // Hợp đồng ICommentRepository.
using CommentAPI.Middleware; // Ghi nhận “lệnh SQL” cho demo CTE.
using Microsoft.AspNetCore.Http; // IHttpContextAccessor lấy HttpContext hiện tại.
using Microsoft.EntityFrameworkCore; // EF Core: AsNoTracking, ToListAsync, v.v.

namespace CommentAPI.Repositories;

public class CommentRepository : RepositoryBase<Comment>, ICommentRepository
{ 
    // Mở khối lớp CommentRepository.
    private readonly AppDbContext _context; // Ngữ cảnh EF: DbSet và SaveChanges.
    private readonly IHttpContextAccessor _httpContextAccessor; // Truy cập HttpContext để middleware đếm SQL.

    public CommentRepository(AppDbContext context, IHttpContextAccessor httpContextAccessor) // Tiêm dependency qua constructor.
        : base(context)
    { // Mở khối constructor.
        _context = context; // Gán DbContext dùng cho mọi truy vấn EF trong lớp này.
        _httpContextAccessor = httpContextAccessor; // Dùng để báo cáo lệnh ADO thô (CTE) vào bộ đếm request.
    } // Kết thúc constructor.

    #region Route Functions

    // [01] Route: GET /api/comments (một hàm xử lý mọi input của route: postId/content/page/date).
    public async Task<(List<Comment> Items, long TotalCount)> GetCommentsRoutePagedAsync( // Phân trang toàn bảng Comments.
        Guid? postId, // null = toàn hệ; có giá trị = trong post.
        string? contentContains, // null/rỗng = list thường; có giá trị = search theo content.
        int page, // Số trang (1-based).
        int pageSize, // Số dòng mỗi trang.
        CancellationToken cancellationToken = default, // Hủy bất đồng bộ.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetPagedAsync.
        var q = ApplyUniversalFilter(
            _context.Comments.AsNoTracking(),
            postId: postId,
            contentContains: contentContains,
            createdAtFrom: createdAtFrom,
            createdAtTo: createdAtTo); // Nguồn + post + content + khoảng thời gian.
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
    } // Kết thúc GetCommentsRoutePagedAsync.

    // [02] Route: GET /api/comments/{id} (service cũng tái dùng cho đọc theo post nội bộ).
    public Task<CommentDto?> GetCommentByIdRouteReadAsync(Guid id, Guid? postId = null, CancellationToken cancellationToken = default) => // Chiếu một comment sang DTO.
        _context.Comments // DbSet.
            .AsNoTracking() // Không track.
            .Where(c => c.Id == id && (postId == null || c.PostId == postId)) // Lọc theo id, tùy chọn thêm postId.
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

    // [03] Route: GET /api/comments/user/{userId}.
    public async Task<(List<Comment> Items, long TotalCount)> GetCommentsByUserRoutePagedAsync( // Phân trang comment của một user.
        Guid userId, // Tác giả (UserId).
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetByUserIdPagedAsync.
        var q = ApplyUniversalFilter(
            _context.Comments.AsNoTracking(),
            userId: userId,
            createdAtFrom: createdAtFrom,
            createdAtTo: createdAtTo); // Thu hẹp theo tác giả + khoảng thời gian.
        var total = await q.LongCountAsync(cancellationToken); // Đếm khớp.
        var items = await q // Cùng bộ lọc.
            .OrderBy(c => c.PostId) // Ổn định theo bài.
            .ThenBy(c => c.CreatedAt) // Rồi thời gian.
            .ThenBy(c => c.Id) // Tie-breaker.
            .Skip((page - 1) * pageSize) // OFFSET.
            .Take(pageSize) // FETCH.
            .ToListAsync(cancellationToken); // Trang.
        return (items, total); // Tuple.
    } // Kết thúc GetCommentsByUserRoutePagedAsync.

    // [04] Route: POST /api/comments

    // [05] Route: PUT /api/comments/{id}

    // [06] Route: PUT /api/admin/comments/{id} (nạp tracked theo post để đổi subtree postId).
    public async Task<List<Comment>> GetCommentsByPostTrackedForAdminRouteAsync( // Truy vấn tracked để SaveChanges cập nhật hàng loạt.
        Guid postId, // Bài viết.
        CancellationToken cancellationToken = default) => // Biểu thức thân phương thức bất đồng bộ.
        await _context.Comments // DbSet (tracked mặc định khi không AsNoTracking).
            .Where(x => x.PostId == postId) // Lọc theo post.
            .OrderBy(x => x.CreatedAt) // Thứ tự ổn định.
            .ThenBy(x => x.Id) // Tie-breaker.
            .ToListAsync(cancellationToken); // Materialize danh sách tracked.

    // [07] Route: DELETE /api/comments/{id}

    // [08][10][12] Route: dữ liệu thô EF — service gọi với rootsOnly/loadCommentsForRootPosts tương ứng từng route.
    public async Task<(List<Comment> Items, long TotalCount, List<Comment> RelatedComments)> LoadRawFlatAsync(
        Guid? postId, // Lọc theo bài viết; null = toàn hệ thống.
        int page, // Số trang bắt đầu từ 1.
        int pageSize, // Số bản ghi mỗi trang.
        bool rootsOnly, // true: chỉ lấy comment gốc (ParentId == null); false: mọi comment khớp bộ lọc (ví dụ [08]).
        bool loadCommentsForRootPosts, // true: sau khi có trang gốc, nạp thêm mọi comment trong các PostId của trang đó ([12]).
        CancellationToken cancellationToken = default, // Cho phép hủy truy vấn.
        DateTime? createdAtFrom = null, // Cận dưới CreatedAt (bao gồm).
        DateTime? createdAtTo = null) // Cận trên CreatedAt (bao gồm).
    { // Bắt đầu pipeline nạp dữ liệu thô entity cho nhánh EF flat/tree.
        var q = ApplyUniversalFilter( // Gom mọi điều kiện lọc vào một IQueryable<Comment>.
            _context.Comments.AsNoTracking(), // Chỉ đọc; không theo dõi thay đổi (hiệu năng + an toàn cho read-only).
            postId: postId, // Thu hẹp theo PostId nếu tham số có giá trị.
            isRoot: rootsOnly ? true : null, // Nếu rootsOnly: ép chỉ gốc; null: không thêm điều kiện gốc/con.
            createdAtFrom: createdAtFrom, // Áp lọc ngày từ (nếu có).
            createdAtTo: createdAtTo); // Áp lọc ngày đến (nếu có).
        var total = await q.LongCountAsync(cancellationToken); // Đếm tổng bản ghi khớp bộ lọc (phục vụ phân trang ở service/API).
        var items = await q // Dùng lại cùng query đã lọc để lấy đúng một trang.
            .OrderBy(c => c.PostId) // Sắp ổn định theo bài viết trước.
            .ThenBy(c => c.CreatedAt) // Trong cùng post, theo thời điểm tạo.
            .ThenBy(c => c.Id) // Tie-break khi CreatedAt trùng.
            .Skip((page - 1) * pageSize) // Bỏ qua các dòng của các trang trước (OFFSET).
            .Take(pageSize) // Giới hạn số dòng (FETCH).
            .ToListAsync(cancellationToken); // Thực thi SQL và materialize List<Comment>.

        if (!loadCommentsForRootPosts || !rootsOnly || items.Count == 0) // Không cần nạp thêm “cây”, hoặc không phải chế độ gốc, hoặc trang rỗng.
        {
            return (items, total, new List<Comment>()); // Trả trang + tổng; RelatedComments để trống.
        }

        var postIds = items.Select(x => x.PostId).Distinct().ToList(); // Tập PostId duy nhất xuất hiện trên trang gốc hiện tại.
        var related = await GetCommentsForPostsAsync(postIds, cancellationToken); // Một truy vấn IN: mọi comment thuộc các post đó (phục vụ build tree/flatten ở service).
        return (items, total, related); // Trả ba thành phần: trang gốc, tổng gốc, toàn bộ comment thô liên quan.
    } // Kết thúc LoadRawFlatAsync.

    // [09][11][13] Route: dữ liệu thô CTE (BFS + Level).
    public async Task<List<CommentFlatDto>> LoadRawCteAsync(
        Guid? postId, // Một post hoặc null để toàn cục.
        CancellationToken cancellationToken = default, // Hủy truy vấn.
        DateTime? createdAtFrom = null, // Lọc CreatedAt trên từng hàng phẳng sau BFS.
        DateTime? createdAtTo = null) // Lọc CreatedAt trên từng hàng phẳng sau BFS.
    { // Bắt đầu tái tạo semantics CTE trong bộ nhớ (không đọc file .sql).
        var allComments = await _context.Comments // Truy vấn DbSet Comments.
            .AsNoTracking() // Không track entity.
            .ToListAsync(cancellationToken); // Nạp toàn bộ bảng vào RAM (mô hình demo/so sánh).

        var source = postId is { } pid // Kiểm tra pattern: có PostId cụ thể.
            ? allComments.Where(c => c.PostId == pid).ToList() // Thu hẹp danh sách entity theo một bài.
            : allComments; // Giữ toàn bộ nếu không truyền postId.

        var rows = BuildCteRows(source, c => c.ParentId == null); // BFS từ mọi gốc: sinh danh sách phẳng có Level.
        var createdAtPredicate = BuildCteDateFilter(createdAtFrom, createdAtTo); // Biên dịch predicate lọc ngày trên CommentFlatDto.
        var result = rows // Chuỗi xử lý LINQ trên bộ nhớ.
            .Where(createdAtPredicate) // Giữ hàng có CreatedAt trong khoảng [from, to] nếu có.
            .OrderBy(x => x.PostId) // Thứ tự giống pipeline trả về: theo bài.
            .ThenBy(x => x.Level) // Rồi theo độ sâu.
            .ThenBy(x => x.CreatedAt) // Rồi thời gian.
            .ThenBy(x => x.Id) // Cuối cùng theo Id để thứ tự tuyệt đối.
            .ToList(); // Materialize List<CommentFlatDto>.

        RequestPerformanceMiddleware.RecordAdoSqlCommand(_httpContextAccessor.HttpContext); // Ghi nhận một “lệnh” cho thống kê request (demo).
        return result; // Trả dữ liệu thô cho service xử lý tree/flatten/phân trang.
    } // Kết thúc LoadRawCteAsync.

    // --- Demo loading (lazy / eager / explicit / projection) ---
    // Hợp đồng chung: cùng một CommentLoadingDemoDto — chỉ cần Post.Title, User.UserName, và số Children trực tiếp.
    // Không nạp Parent: DTO không hiển thị cha; nạp Parent ở một nhánh làm lệch số round-trip và mục tiêu so sánh.
    // Phân trang: cùng COUNT toàn bảng, cùng OrderBy PostId → CreatedAt → Id, cùng Skip/Take.

    // [14] Route: GET /api/comments/demo/lazy-loading (mọi mode paginationEnabled).
    public async Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsLazyLoadingDemoRouteAsync( // Demo lazy một hàm cho mọi mode.
        bool paginationEnabled, // true = phân trang, false = lấy toàn bộ.
        int page, // Trang (chỉ dùng khi paginationEnabled=true).
        int pageSize, // Cỡ trang (chỉ dùng khi paginationEnabled=true).
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Lọc theo bài (tùy chọn).
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối.
        var q = ApplyUniversalFilter(
            _context.Comments, // Tracked set (không AsNoTracking) để lazy sau này.
            postId: postId,
            createdAtFrom: createdAtFrom,
            createdAtTo: createdAtTo); // Thu hẹp theo postId (nếu có) + khoảng thời gian.
        var ordered = q // Query cơ sở dùng chung cho cả hai mode.
            .OrderBy(c => c.PostId) // Sắp theo bài để thứ tự ổn định giữa các lần gọi.
            .ThenBy(c => c.CreatedAt) // Trong cùng post, theo thời điểm tạo.
            .ThenBy(c => c.Id); // Tie-break khi CreatedAt trùng.
        List<Comment> rows; // Sẽ chứa dòng comment sau khi nạp (một trang hoặc toàn bộ).
        long total; // Tổng số dòng dùng cho metadata phân trang hoặc totalCount unpaged.
        if (paginationEnabled) // Nhánh bật Skip/Take.
        {
            total = await q.LongCountAsync(cancellationToken); // Đếm trên query gốc (trước OrderBy materialize) để tổng đúng bộ lọc.
            rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken); // Chỉ nạp một trang đã sắp.
        }
        else // Nhánh unpaged: lấy mọi dòng khớp filter.
        {
            rows = await ordered.ToListAsync(cancellationToken); // Nạp toàn bộ vào RAM (cẩn trọng kích thước dữ liệu).
            total = rows.Count; // Tổng chính là số phần tử đã nạp.
        }

        var list = new List<CommentLoadingDemoDto>(rows.Count); // Cấp phát sẵn dung lượng.
        foreach (var comment in rows) // Duyệt từng comment đã nạp.
        { // Mở khối foreach.
            list.Add(new CommentLoadingDemoDto // Mỗi vòng: đọc navigation có thể bắn thêm SQL lazy.
            { // Mở initializer.
                LoadingStrategy = "lazy", // Nhãn.
                CommentId = comment.Id, // Id.
                Content = comment.Content, // Nội dung.
                CreatedAt = comment.CreatedAt, // Thời điểm tạo.
                PostId = comment.PostId, // Post.
                PostTitle = comment.Post?.Title, // Có thể lazy load Post.
                UserId = comment.UserId, // User.
                AuthorUserName = comment.User?.UserName, // Có thể lazy User.
                ParentId = comment.ParentId, // Cha.
                ChildrenCount = comment.Children.Count // Có thể lazy Children.
            }); // Kết thúc Add.
        } // Kết thúc foreach.

        return (list, total); // Tuple kết quả và tổng.
    } // Kết thúc GetCommentsLazyLoadingDemoRouteAsync.

    // [15] Route: GET /api/comments/demo/eager-loading (mọi mode paginationEnabled).
    public async Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsEagerLoadingDemoRouteAsync( // Demo eager một hàm cho mọi mode.
        bool paginationEnabled, // true = phân trang, false = lấy toàn bộ.
        int page, // Trang (chỉ dùng khi paginationEnabled=true).
        int pageSize, // Cỡ trang (chỉ dùng khi paginationEnabled=true).
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Lọc post tùy chọn.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối.
        var baseQuery = ApplyUniversalFilter(
            _context.Comments // DbSet.
                .AsNoTracking(), // Không track.
            postId: postId,
            createdAtFrom: createdAtFrom,
            createdAtTo: createdAtTo); // Thu hẹp theo postId (nếu có) + khoảng thời gian.
        var total = paginationEnabled // Nếu phân trang thì đếm trước trên query chưa Include (tránh đếm sai do join).
            ? await baseQuery.LongCountAsync(cancellationToken) // Tổng dòng khớp filter thuần.
            : 0L; // Unpaged: tạm 0; sẽ gán = list.Count sau khi nạp.
        var query = baseQuery // Bắt đầu từ query đã lọc.
            .Include(c => c.Post) // Nạp Post.
            .Include(c => c.User) // Nạp User.
            .Include(c => c.Children) // Nạp Children (không Include Parent — đồng bộ demo).
            .AsSplitQuery() // Tách query.
            .OrderBy(c => c.PostId) // Sắp trang.
            .ThenBy(c => c.CreatedAt) // Thời gian.
            .ThenBy(c => c.Id); // Id.
        var rows = paginationEnabled // Chọn nhánh materialize.
            ? await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken) // Một trang sau Include.
            : await query.ToListAsync(cancellationToken); // Toàn bộ sau Include (nhiều round-trip split).

        var list = rows.ConvertAll(comment => new CommentLoadingDemoDto // Ánh xạ trong RAM, không SQL.
        { // Mở initializer.
            LoadingStrategy = "eager", // Nhãn eager.
            CommentId = comment.Id, // Id.
            Content = comment.Content, // Nội dung.
            CreatedAt = comment.CreatedAt, // Thời điểm tạo.
            PostId = comment.PostId, // Post.
            PostTitle = comment.Post?.Title, // Đã Include.
            UserId = comment.UserId, // User.
            AuthorUserName = comment.User?.UserName, // Đã Include.
            ParentId = comment.ParentId, // Cha.
            ChildrenCount = comment.Children.Count // Đã nạp collection.
        }); // Kết thúc ConvertAll.

        if (!paginationEnabled) // Ở chế độ unpaged.
        {
            total = list.Count; // Tổng chính là số phần tử đã nạp và map.
        }
        return (list, total); // Trả danh sách và tổng.
    } // Kết thúc GetCommentsEagerLoadingDemoRouteAsync.

    // [16] Route: GET /api/comments/demo/explicit-loading (mọi mode paginationEnabled).
    public async Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsExplicitLoadingDemoRouteAsync( // Demo explicit một hàm cho mọi mode.
        bool paginationEnabled, // true = phân trang, false = lấy toàn bộ.
        int page, // Trang (chỉ dùng khi paginationEnabled=true).
        int pageSize, // Cỡ trang (chỉ dùng khi paginationEnabled=true).
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Lọc post tùy chọn.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối.
        var q = ApplyUniversalFilter(
            _context.Comments, // Tracked.
            postId: postId,
            createdAtFrom: createdAtFrom,
            createdAtTo: createdAtTo); // Cùng phạm vi với lazy demo.
        var ordered = q // Query cơ sở dùng chung.
            .OrderBy(c => c.PostId) // Sắp post.
            .ThenBy(c => c.CreatedAt) // Thời gian.
            .ThenBy(c => c.Id); // Id.
        List<Comment> rows; // Buffer chứa entity sau khi nạp.
        long total; // Tổng dòng hoặc độ dài list unpaged.
        if (paginationEnabled) // Bật phân trang.
        {
            total = await q.LongCountAsync(cancellationToken); // COUNT trên filter gốc.
            rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken); // Một trang đã sắp.
        }
        else // Unpaged.
        {
            rows = await ordered.ToListAsync(cancellationToken); // Nạp mọi dòng khớp filter.
            total = rows.Count; // Tổng = số phần tử.
        }

        var list = new List<CommentLoadingDemoDto>(rows.Count); // Danh sách đích.
        foreach (var comment in rows) // Từng dòng trang.
        { // Mở khối.
            // Mỗi comment: 3 lần LoadAsync — tương ứng 3 lần lazy (Post, User, Children) trên cùng một trang.
            await _context.Entry(comment).Reference(c => c.Post).LoadAsync(cancellationToken); // SQL: nạp Post.
            await _context.Entry(comment).Reference(c => c.User).LoadAsync(cancellationToken); // SQL: nạp User.
            await _context.Entry(comment).Collection(c => c.Children).LoadAsync(cancellationToken); // SQL: nạp Children.

            list.Add(new CommentLoadingDemoDto // Thêm DTO sau khi nạp.
            { // Mở initializer.
                LoadingStrategy = "explicit", // Nhãn.
                CommentId = comment.Id, // Id.
                Content = comment.Content, // Nội dung.
                CreatedAt = comment.CreatedAt, // Thời điểm tạo.
                PostId = comment.PostId, // Post.
                PostTitle = comment.Post?.Title, // Đã Load.
                UserId = comment.UserId, // User.
                AuthorUserName = comment.User?.UserName, // Đã Load.
                ParentId = comment.ParentId, // Cha.
                ChildrenCount = comment.Children.Count // Đã Load collection.
            }); // Kết thúc Add.
        } // Kết thúc foreach.

        return (list, total); // Trả tuple.
    } // Kết thúc GetCommentsExplicitLoadingDemoRouteAsync.

    // [17] Route: GET /api/comments/demo/projection (mọi mode paginationEnabled).
    public async Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsProjectionDemoRouteAsync( // Demo projection một hàm cho mọi mode.
        bool paginationEnabled, // true = phân trang, false = lấy toàn bộ.
        int page, // Trang (chỉ dùng khi paginationEnabled=true).
        int pageSize, // Cỡ trang (chỉ dùng khi paginationEnabled=true).
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Lọc post tùy chọn.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối.
        var q = ApplyUniversalFilter(
            _context.Comments // DbSet.
                .AsNoTracking(), // Không track.
            postId: postId,
            createdAtFrom: createdAtFrom,
            createdAtTo: createdAtTo); // Filter một bài nếu có postId + khoảng thời gian.
        var total = paginationEnabled // Đếm trước khi Select nếu paged.
            ? await q.LongCountAsync(cancellationToken) // Tổng khớp filter trên entity.
            : 0L; // Unpaged: gán sau từ items.Count.
        var query = q // Tiếp từ q.
            .OrderBy(c => c.PostId) // Sắp.
            .ThenBy(c => c.CreatedAt) // Thời gian.
            .ThenBy(c => c.Id) // Id.
            .Select(c => new CommentLoadingDemoDto // Toàn bộ phân trang + projection trên server.
            { // Mở initializer.
                LoadingStrategy = "projection", // Nhãn.
                CommentId = c.Id, // Id.
                Content = c.Content, // Nội dung.
                CreatedAt = c.CreatedAt, // Thời điểm tạo.
                PostId = c.PostId, // Post.
                PostTitle = c.Post != null ? c.Post.Title : null, // Title trong SQL.
                UserId = c.UserId, // User.
                AuthorUserName = c.User != null ? c.User.UserName : null, // UserName trong SQL.
                ParentId = c.ParentId, // Cha.
                ChildrenCount = c.Children.Count() // Đếm con trong SQL.
            }); // Kết thúc Select projection.
        var items = paginationEnabled // Materialize IQueryable<CommentLoadingDemoDto>.
            ? await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken) // Một trang DTO (SQL đã projection).
            : await query.ToListAsync(cancellationToken); // Toàn bộ DTO trong một (hoặc vài) câu SQL.
        if (!paginationEnabled) // Unpaged.
        {
            total = items.Count; // Đồng bộ tổng với số phần tử trả về.
        }
        return (items, total); // Trả trang DTO và tổng.
    } // Kết thúc GetCommentsProjectionDemoRouteAsync.


    #endregion

    #region Helpers

    // Helper support: [01][14][15][16][17] validate postId.
    public async Task<bool> PostExistsAsync(Guid postId) // Kiểm tra bài viết tồn tại.
    { // Mở khối PostExistsAsync.
        return await _context.Posts // DbSet Posts.
            .AsNoTracking() // Chỉ đọc.
            .AnyAsync(x => x.Id == postId); // Kiểm tra một Id post tồn tại.
    } // Kết thúc PostExistsAsync.

    // Helper support: [03][04][06] validate userId.
    public async Task<bool> UserExistsAsync(Guid userId) // Kiểm tra người dùng tồn tại.
    { // Mở khối UserExistsAsync.
        return await _context.Users // DbSet Users (Identity).
            .AsNoTracking() // Chỉ đọc.
            .AnyAsync(x => x.Id == userId); // Kiểm tra user tồn tại.
    } // Kết thúc UserExistsAsync.

    // Helper support: [04][06] validate parent comment cùng post.
    public async Task<bool> ParentExistsAsync(Guid parentId, Guid postId) // Cha phải cùng post.
    { // Mở khối ParentExistsAsync.
        return await _context.Comments // DbSet Comments.
            .AsNoTracking() // Chỉ đọc.
            .AnyAsync(x => x.Id == parentId && x.PostId == postId); // Cha phải cùng post với con.
    } // Kết thúc ParentExistsAsync.

    // Helper support: [10][12] nạp toàn bộ comment theo post/all để dựng tree, flatten.
    public async Task<List<Comment>> GetCommentsRouteAllAsync(
        Guid? postId = null, // null: mọi post; có giá trị: chỉ một bài.
        DateTime? createdAtFrom = null, // Lọc CreatedAt từ.
        DateTime? createdAtTo = null) // Lọc CreatedAt đến.
    { // Nạp toàn bộ entity khớp bộ lọc (không phân trang tại đây).
        var q = ApplyUniversalFilter(_context.Comments.AsNoTracking(), postId: postId, createdAtFrom: createdAtFrom, createdAtTo: createdAtTo); // IQueryable đã lọc post + ngày.
        return await q // Tiếp tục từ cùng nguồn.
            .OrderBy(x => x.CreatedAt) // Thứ tự đơn giản theo thời gian (service có thể dựng cây theo ParentId).
            .ToListAsync(); // Thực thi và trả List (sync-over-async không dùng ở đây — gọi từ async caller).
    } // Kết thúc GetCommentsRouteAllAsync.

    // Helper support: [01] nhánh unpaged/search nội bộ service.
    public async Task<List<Comment>> SearchCommentsRouteAllAsync(
        Guid? postId, // Giới hạn post tùy chọn.
        string contentContains, // Chuỗi tìm trong Content (Contains).
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc ngày từ.
        DateTime? createdAtTo = null) // Lọc ngày đến.
    { // Trả mọi comment khớp post + content + khoảng ngày.
        var q = ApplyUniversalFilter( // Pipeline lọc thống nhất.
            _context.Comments.AsNoTracking(), // Read-only.
            postId: postId, // Lọc post nếu có.
            contentContains: contentContains, // Điều kiện Contains trên nội dung.
            createdAtFrom: createdAtFrom, // Cận dưới CreatedAt.
            createdAtTo: createdAtTo); // Cận trên CreatedAt.
        return await q // Materialize sau khi sắp.
            .OrderBy(c => c.PostId) // Ổn định theo bài.
            .ThenBy(c => c.CreatedAt) // Rồi thời gian.
            .ThenBy(c => c.Id) // Tie-break.
            .ToListAsync(cancellationToken); // Trả danh sách đầy đủ (unpaged).
    } // Kết thúc SearchCommentsRouteAllAsync.

    // Helper nội bộ: nạp comment cho nhiều post bằng một truy vấn IN.
    private async Task<List<Comment>> GetCommentsForPostsAsync(
        IReadOnlyCollection<Guid> postIds, // Danh sách PostId cần nạp hết comment.
        CancellationToken cancellationToken = default) // Hủy.
    { // Một round-trip SQL thay vì N truy vấn từng post.
        if (postIds.Count == 0) // Không có post nào.
        {
            return new List<Comment>(); // Trả rỗng sớm, tránh SQL vô nghĩa.
        }

        return await _context.Comments // DbSet.
            .AsNoTracking() // Không track.
            .Where(c => postIds.Contains(c.PostId)) // Điều kiện IN trên PostId.
            .OrderBy(c => c.PostId) // Gom nhóm theo bài khi đọc.
            .ThenBy(c => c.CreatedAt) // Trong post, theo thời gian.
            .ThenBy(c => c.Id) // Tie-break.
            .ToListAsync(cancellationToken); // Trả toàn bộ comment thuộc các post đã liệt kê.
    } // Kết thúc GetCommentsForPostsAsync.

    // Universal filter duy nhất cho Comment: gom điều kiện route vào một pipeline IQueryable.
    private static IQueryable<Comment> ApplyUniversalFilter(
        IQueryable<Comment> query, // Nguồn truy vấn gốc (DbSet hoặc đã Where sẵn).
        Guid? postId = null, // Lọc theo PostId nếu có.
        Guid? userId = null, // Lọc theo UserId (tác giả) nếu có.
        string? contentContains = null, // Lọc Contains trên Content nếu chuỗi khác null/blank.
        Guid? parentId = null, // Lọc theo ParentId cụ thể nếu có.
        bool? isRoot = null, // true: chỉ gốc; false: chỉ không phải gốc; null: bỏ qua.
        DateTime? createdAtFrom = null, // CreatedAt >= from nếu có.
        DateTime? createdAtTo = null) // CreatedAt <= to nếu có.
    { // Trả IQueryable mới; không thực thi SQL tại đây.
        if (postId is { } pid) // Pattern matching: có Guid post.
        {
            query = query.Where(c => c.PostId == pid); // Thu hẹp theo một bài viết.
        }

        if (userId is { } uid) // Có Guid user.
        {
            query = query.Where(c => c.UserId == uid); // Chỉ comment của tác giả đó.
        }

        if (!string.IsNullOrWhiteSpace(contentContains)) // Chuỗi tìm kiếm hợp lệ.
        {
            query = query.Where(c => c.Content.Contains(contentContains)); // Tìm kiếm chuỗi con trong nội dung.
        }

        if (parentId is { } parent) // Có Id comment cha.
        {
            query = query.Where(c => c.ParentId == parent); // Lọc con trực tiếp của cha đó.
        }

        if (isRoot is { } root) // Có yêu cầu lọc gốc/không gốc.
        {
            query = root // Nếu root == true.
                ? query.Where(c => c.ParentId == null) // Chỉ các comment không có cha (gốc cây).
                : query.Where(c => c.ParentId != null); // Ngược lại: chỉ comment có cha.
        }

        if (createdAtFrom is { } from) // Có cận dưới ngày.
        {
            query = query.Where(c => c.CreatedAt >= from); // Inclusive: lớn hơn hoặc bằng from.
        }

        if (createdAtTo is { } to) // Có cận trên ngày.
        {
            query = query.Where(c => c.CreatedAt <= to); // Inclusive: nhỏ hơn hoặc bằng to.
        }

        return query; // Trả pipeline cuối để caller OrderBy/Skip/Take/ToListAsync.
    } // Kết thúc ApplyUniversalFilter.

    // Ánh xạ entity + level sang hàng phẳng như output CTE.
    private static CommentFlatDto MapToFlatRow(Comment c, int level) => new() // Một dòng trong “kết quả CTE” giả lập.
    {
        Id = c.Id, // Khóa comment.
        Content = c.Content, // Nội dung.
        CreatedAt = c.CreatedAt, // Thời điểm tạo (dùng cho lọc sau BFS).
        ParentId = c.ParentId, // Tham chiếu cha (null nếu gốc).
        PostId = c.PostId, // Thuộc bài viết nào.
        UserId = c.UserId, // Tác giả.
        Level = level // Độ sâu BFS so với gốc (0 tại gốc).
    }; // Kết thúc biểu thức MapToFlatRow.

    // Duyệt cây theo level-order (BFS) để tái tạo semantics CTE đệ quy: chỉ lấy node reachable từ roots.
    private static List<CommentFlatDto> BuildCteRows(
        IReadOnlyList<Comment> sourceComments, // Tập entity đã nạp (một post hoặc toàn hệ).
        Func<Comment, bool> rootPredicate) // Điều kiện nhận diện gốc (thường ParentId == null).
    { // Sinh danh sách phẳng có Level, thứ tự duyệt giống mở rộng CTE.
        var rows = new List<CommentFlatDto>(sourceComments.Count); // Dự trữ dung lượng tối đa bằng số entity (thường ít hơn sau lọc).
        var childrenByParentId = sourceComments // Xây chỉ mục con theo ParentId để tra O(1).
            .Where(c => c.ParentId is not null) // Bỏ gốc khỏi nhóm “là con”.
            .GroupBy(c => c.ParentId!.Value) // Gom theo Id cha.
            .ToDictionary( // Dictionary ParentId → danh sách con đã sắp.
                g => g.Key, // Khóa là Id cha.
                g => g.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToList()); // Con cùng cha: thứ tự ổn định.

        var queue = new Queue<(Comment Node, int Level)>(); // Hàng đợi BFS: nút + độ sâu.
        var visited = new HashSet<Guid>(); // Tránh xử lý lặp (chu trình / dữ liệu lệch).
        foreach (var root in sourceComments // Duyệt mọi ứng viên gốc theo predicate.
                     .Where(rootPredicate) // Ví dụ ParentId == null.
                     .OrderBy(x => x.CreatedAt) // Thứ tự enqueue gốc ổn định.
                     .ThenBy(x => x.Id)) // Tie-break.
        {
            if (visited.Add(root.Id)) // Add trả true nếu Id chưa có trong visited.
            {
                queue.Enqueue((root, 0)); // Đưa gốc vào hàng đợi với Level 0.
            }
        }

        while (queue.Count > 0) // Còn nút để duyệt.
        {
            var (current, level) = queue.Dequeue(); // Lấy nút tiếp theo và độ sâu hiện tại.
            rows.Add(MapToFlatRow(current, level)); // Ghi một hàng phẳng tương ứng nút.

            if (!childrenByParentId.TryGetValue(current.Id, out var children)) // Không có con trong tập nguồn.
            {
                continue; // Bỏ qua mở rộng nhánh.
            }

            foreach (var child in children) // Duyệt từng con đã sắp của current.
            {
                // Khớp SQL CTE: child phải cùng PostId với parent khi nối đệ quy.
                if (child.PostId != current.PostId) // Tránh nối nhầm giữa hai bài.
                {
                    continue; // Bỏ qua cạnh không hợp lệ.
                }

                if (visited.Add(child.Id)) // Con chưa thăm.
                {
                    queue.Enqueue((child, level + 1)); // Đưa con vào BFS với Level tăng 1.
                }
            }
        }

        return rows; // Danh sách hàng phẳng đầy đủ từ mọi gốc reachable.
    } // Kết thúc BuildCteRows.

    // Khai báo hàm nhận vào range 2 input createdAt (có thể null) và trả về một Delegate (hàm) để kiểm tra điều kiện
    private static Func<CommentFlatDto, bool> BuildCteDateFilter(DateTime? createdAtFrom, DateTime? createdAtTo)
    {
        // 1. Tạo tham số đầu vào cho biểu thức lambda, tương đương với biến "x" trong (x => ...)
        // Kiểu dữ liệu của x là CommentFlatDto
        var parameter = Expression.Parameter(typeof(CommentFlatDto), "x");

        // 2. Khởi tạo phần thân của biểu thức là một hằng số 'true'. 
        // Nếu không có bộ lọc nào được truyền vào, nó sẽ luôn trả về true (lấy tất cả dữ liệu).
        Expression body = Expression.Constant(true);

        // 3. Truy cập vào thuộc tính 'CreatedAt' của tham số 'x' (tương đương x.CreatedAt)
        var createdAtProp = Expression.Property(parameter, nameof(CommentFlatDto.CreatedAt));

        // 4. Kiểm tra nếu ngày bắt đầu (createdAtFrom) có giá trị (không null)
        if (createdAtFrom is { } from)
        {
            // Tạo biểu thức so sánh: x.CreatedAt >= createdAtFrom
            var fromExpr = Expression.GreaterThanOrEqual(createdAtProp, Expression.Constant(from));

            // Gộp vào biểu thức chính bằng phép AND: body = (true && x.CreatedAt >= from)
            body = Expression.AndAlso(body, fromExpr);
        }

        // 5. Kiểm tra nếu ngày kết thúc (createdAtTo) có giá trị (không null)
        if (createdAtTo is { } to)
        {
            // Tạo biểu thức so sánh: x.CreatedAt <= createdAtTo
            var toExpr = Expression.LessThanOrEqual(createdAtProp, Expression.Constant(to));

            // Tiếp tục gộp vào biểu thức chính bằng phép AND: body = (body && x.CreatedAt <= to)
            body = Expression.AndAlso(body, toExpr);
        }

        // 6. Xây dựng một biểu thức Lambda hoàn chỉnh: (CommentFlatDto x) => body
        var lambda = Expression.Lambda<Func<CommentFlatDto, bool>>(body, parameter);

        // 7. Biên dịch (Compile) cái "cây biểu thức" này thành một hàm thực thi (Delegate) 
        // để có thể sử dụng được trong các hàm như .Where() của List/IEnumerable
        return lambda.Compile();
    }

    #endregion
} // Kết thúc lớp CommentRepository.
