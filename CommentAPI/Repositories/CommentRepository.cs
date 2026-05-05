using CommentAPI; // SortByColumn.
using CommentAPI.Data; // DbContext ứng dụng.
using CommentAPI.DTOs; // DTO trả về / projection.
using CommentAPI.Entities; // Entity Comment và liên quan.
using CommentAPI.Interfaces; // Hợp đồng ICommentRepository.
using Microsoft.EntityFrameworkCore; // EF Core: AsNoTracking, SqlQueryRaw, ToListAsync, v.v.

namespace CommentAPI.Repositories;

// partial: logic sort tách file CommentRepository.Sorting.cs (ApplyUniversalSorting + parse).
public partial class CommentRepository : RepositoryBase<Comment>, ICommentRepository
{ 
    // Mở khối lớp CommentRepository.
    private readonly AppDbContext _context; // Ngữ cảnh EF: DbSet và SaveChanges.

    public CommentRepository(AppDbContext context) // Tiêm dependency qua constructor.
        : base(context)
    { // Mở khối constructor (base(context) đã chạy trước khi vào thân).
        // BƯỚC 1 — Lưu AppDbContext cục bộ: DbSet Comments/Posts/Users + SqlQueryRaw dùng cùng instance với RepositoryBase.
        _context = context; // Gán DbContext dùng cho mọi truy vấn EF trong lớp này.
    } // Kết thúc constructor.

    #region Route Functions

    // Đếm mọi comment khớp bộ lọc route (metadata TotalComments; đồng bộ với LoadFlatAsync).
    public async Task<long> CountCommentsMatchingRouteAsync(
        Guid? postId, // Lọc bài hoặc null.
        string? contentContains, // Contains nội dung hoặc null.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null, // Lọc CreatedAt.
        Guid? userId = null) // Lọc tác giả.
    { // Mở khối CountCommentsMatchingRouteAsync.
        // BƯỚC 1 — Gom mọi điều kiện route vào IQueryable chỉ đọc (chưa SQL).
        var filtered = ApplyUniversalFilter(
            _context.Comments.AsNoTracking(), // Chỉ đọc.
            postId: postId, // Post.
            userId: userId, // User.
            contentContains: contentContains, // Nội dung.
            createdAtFrom: createdAtFrom, // Từ ngày.
            createdAtTo: createdAtTo); // Đến ngày.
        // BƯỚC 2 — Thực thi LongCountAsync: một round-trip COUNT(*) khớp lọc.
        return await filtered.LongCountAsync(cancellationToken);
    } // Kết thúc CountCommentsMatchingRouteAsync.

    // Đếm comment gốc (ParentId null) khớp bộ lọc — đối chiếu với số “node” cấp một trong DB.
    public async Task<long> CountCommentRootsMatchingRouteAsync(
        Guid? postId, // Lọc bài.
        string? contentContains, // Contains.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null, // Lọc CreatedAt.
        Guid? userId = null) // Lọc tác giả.
    { // Mở khối CountCommentRootsMatchingRouteAsync.
        // BƯỚC 1 — Gom lọc + isRoot: true (chỉ comment gốc); IQueryable chưa SQL.
        var filtered = ApplyUniversalFilter(
            _context.Comments.AsNoTracking(), // Chỉ đọc.
            postId: postId, // Post.
            userId: userId, // User.
            contentContains: contentContains, // Nội dung.
            isRoot: true, // Chỉ gốc cây.
            createdAtFrom: createdAtFrom, // Từ ngày.
            createdAtTo: createdAtTo); // Đến ngày.
        // BƯỚC 2 — LongCountAsync trên tập gốc đã lọc (TotalNodes / mẫu số TotalPages theo gốc).
        return await filtered.LongCountAsync(cancellationToken);
    } // Kết thúc CountCommentRootsMatchingRouteAsync.

    // Phân trang theo gốc (cùng bộ lọc với CountCommentRootsMatchingRouteAsync).
    public async Task<(List<Comment> Items, long TotalRootCount)> GetCommentRootsRoutePagedAsync(
        Guid? postId,
        string? contentContains,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        SortByColumn? sort = null)
    { // Mở khối GetCommentRootsRoutePagedAsync — phân trang trên tập comment gốc đã lọc.
        // BƯỚC 1 — Xây IQueryable comment gốc (ParentId null) đã lọc post/user/content/ngày; chưa thực thi SQL.
        var q = ApplyUniversalFilter( // Gom điều kiện post/user/content/ngày.
            _context.Comments.AsNoTracking(), // Chỉ đọc, không track.
            postId: postId, // Thu hẹp một bài nếu có.
            userId: userId, // Lọc tác giả nếu có.
            contentContains: contentContains, // Contains nội dung nếu có.
            isRoot: true, // Chỉ ParentId == null.
            createdAtFrom: createdAtFrom, // Khoảng CreatedAt.
            createdAtTo: createdAtTo); // Khoảng CreatedAt.
        // BƯỚC 2 — COUNT(*) trên cùng biểu thức q: tổng gốc khớp lọc (metadata TotalPages / TotalRootCount).
        var totalRoots = await q.LongCountAsync(cancellationToken); // Tổng số gốc khớp — metadata TotalPages.
        // BƯỚC 3 — ORDER BY LINQ theo sort rồi Skip/Take một trang gốc rồi ToListAsync.
        var s = sort ?? CommentListSortDefault;
        var items = await ApplyUniversalSorting(q, s) // Áp thứ tự cột + hướng từ query.
            .Skip((page - 1) * pageSize) // Bỏ gốc của các trang trước.
            .Take(pageSize) // Giữ đúng số gốc một trang.
            .ToListAsync(cancellationToken); // Thực thi SQL một lần cho trang.
        return (items, totalRoots); // Trả danh sách gốc + tổng gốc.
    } // Kết thúc GetCommentRootsRoutePagedAsync.

    // BFS theo tầng (LINQ trên DbSet): từ Id gốc → duyệt mọi hậu duệ; nhiều câu SQL nhỏ — tương thích SQLite/SQL Server, không cần CTE đệ quy khác nhau giữa provider.
    public async Task<List<Comment>> LoadCommentsForSubtreesAsync(
        IReadOnlyList<Guid> rootIds,
        CancellationToken cancellationToken = default,
        SortByColumn? sort = null)
    {
        // TRƯỜNG HỢP A: Không có gốc — không cần truy vấn DB.
        if (rootIds.Count == 0)
        {
            return new List<Comment>();
        }

        // BƯỚC 1 — Khởi tạo tập kết quả + tập Id đã gặp + biên BFS (frontier) = các Id gốc sau Distinct.
        var result = new List<Comment>(); // Mọi entity đã nạp (gốc + con cháu), không trùng Id.
        var seen = new HashSet<Guid>(); // Đánh dấu Id đã đưa vào result — Add trả false nếu trùng.
        var frontier = rootIds.Distinct().ToList(); // Lớp hiện tại: Id cần SELECT entity đầy đủ ở vòng lặp kế.

        // BƯỚC 2 — Vòng lặp theo tầng — mỗi vòng tối đa hai query: (1) nạp entity theo frontier, (2) tìm Id con có ParentId ∈ frontier.
        while (frontier.Count > 0)
        {
            // BƯỚC 2a: SELECT mọi comment có Id thuộc frontier — materialize vào batch.
            var batch = await _context.Comments.AsNoTracking()
                .Where(c => frontier.Contains(c.Id))
                .ToListAsync(cancellationToken);

            // TRƯỜNG HỢP B: Frontier chứa Id không tồn tại DB (dữ liệu lạc) — batch rỗng → thoát để không lặp vô hạn.
            if (batch.Count == 0)
            {
                break;
            }

            // BƯỚC 2b: Với mỗi entity trong batch — nếu Id chưa có trong seen thì Add vào result.
            foreach (var c in batch)
            {
                if (seen.Add(c.Id)) // Add trả true lần đầu gặp Id.
                {
                    result.Add(c); // Giữ entity đầy đủ cho BuildTreeFlat ở service.
                }
            }

            // BƯỚC 2c: Lấy danh sách Id vừa nạp — dùng làm tập cha để tìm con trực tiếp.
            var parentIds = batch.Select(c => c.Id).ToList();

            // BƯỚC 2d: SELECT Id của mọi comment có ParentId trỏ vào một trong parentIds — chỉ lấy Id để nhẹ.
            var childIds = await _context.Comments.AsNoTracking()
                .Where(c => c.ParentId != null && parentIds.Contains(c.ParentId.Value))
                .Select(c => c.Id)
                .Distinct()
                .ToListAsync(cancellationToken);

            // BƯỚC 2e: Frontier kế = các Id con chưa nằm trong seen (tránh lặp nếu đồ thị có nhiều đường tới cùng nút).
            frontier = childIds.Where(id => !seen.Contains(id)).ToList();
        }

        // BƯỚC 3 — Sắp kết quả theo sort (LINQ to Objects sau BFS) — đồng bộ với thứ tự hiển thị route tree/flat.
        var s = sort ?? CommentListSortDefault;
        return ApplyUniversalSorting(result.AsQueryable(), s).ToList(); // AsQueryable nối OrderBy whitelist.
    }

    // [02] Route: GET /api/comments/{id} (service cũng tái dùng cho đọc theo post nội bộ).
    public async Task<CommentDto?> GetCommentByIdRouteReadAsync(Guid id, Guid? postId = null, CancellationToken cancellationToken = default) // Chiếu một comment sang DTO.
    { // Mở khối GetCommentByIdRouteReadAsync.
        // BƯỚC 1 — Bắt đầu chuỗi LINQ: DbSet Comments + AsNoTracking (chỉ đọc).
        return await _context.Comments // DbSet.
            .AsNoTracking() // Không track.
            // BƯỚC 2 — Lọc theo Id và PostId tùy chọn (không lộ comment sang bài khác khi postId có giá trị).
            .Where(c => c.Id == id && (postId == null || c.PostId == postId)) // Lọc theo id, tùy chọn thêm postId.
            // BƯỚC 3 — Chiếu thẳng sang CommentDto trên SQL (ít cột hơn SELECT * entity).
            .Select(c => new CommentDto // Chiếu thẳng sang DTO trong SQL (không SELECT * entity đầy đủ).
            { // Mở khối initializer.
                Id = c.Id, // Cột Id vào DTO.
                Content = c.Content, // Nội dung.
                CreatedAt = c.CreatedAt, // Thời gian tạo.
                PostId = c.PostId, // Khóa post.
                UserId = c.UserId, // Khóa user.
                ParentId = c.ParentId // Cha (nullable).
            }) // Kết thúc projection object.
            // BƯỚC 4 — FirstOrDefaultAsync: tối đa một dòng hoặc null.
            .FirstOrDefaultAsync(cancellationToken); // SELECT TOP 1 ... hoặc tương đương; null nếu không có.
    } // Kết thúc GetCommentByIdRouteReadAsync.

    // [04] Route: POST /api/comments

    // [05] Route: PUT /api/comments/{id}

    // [06] Route: PUT /api/admin/comments/{id} (nạp tracked theo post để khi đổi postId của một comment, tất cả con cháu của comment đó cũng đều có thể đổi theo).
    public async Task<List<Comment>> GetCommentsByPostTrackedForAdminRouteAsync( // Truy vấn tracked để SaveChanges cập nhật hàng loạt.
        Guid postId, // Bài viết.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetCommentsByPostTrackedForAdminRouteAsync.
        // BƯỚC 1 — DbSet Comments tracked (không AsNoTracking) để sau này gán PostId hàng loạt + SaveChanges.
        return await _context.Comments // DbSet (tracked mặc định khi không AsNoTracking).
            // BƯỚC 2 — Chỉ comment thuộc postId (bài cũ chứa subtree cần cập nhật).
            .Where(x => x.PostId == postId) // Lọc theo post.
            // BƯỚC 3 — Sắp ổn định CreatedAt rồi Id (debug/log nhất quán).
            .OrderBy(x => x.CreatedAt) // Thứ tự ổn định.
            .ThenBy(x => x.Id) // Tie-breaker.
            // BƯỚC 4 — Materialize toàn bộ hàng tracked của post vào RAM.
            .ToListAsync(cancellationToken); // Materialize danh sách tracked.
    } // Kết thúc GetCommentsByPostTrackedForAdminRouteAsync.

    // CTE riêng cho một PostId: neo + đệ quy — phần SELECT cố định; thứ tự dòng áp bằng LINQ sau SqlQueryRaw (không ORDER BY động trong SQL).
    private const string PostCommentsRecursiveCteSqlBody = """
WITH PostCommentTree AS (
    SELECT c.Id, c.Content, c.CreatedAt, c.ParentId, c.PostId, c.UserId, CAST(0 AS INTEGER) AS Level
    FROM Comments AS c
    WHERE c.PostId = {0} AND c.ParentId IS NULL
    UNION ALL
    SELECT c2.Id, c2.Content, c2.CreatedAt, c2.ParentId, c2.PostId, c2.UserId, pct.Level + 1
    FROM Comments AS c2
    INNER JOIN PostCommentTree AS pct ON c2.ParentId = pct.Id AND c2.PostId = pct.PostId
)
SELECT Id, Content, CreatedAt, ParentId, PostId, UserId, Level
FROM PostCommentTree
""";

    // CTE một lớp: chỉ gốc trong bài {0} — thân câu lệnh cố định; sort LINQ sau khi materialize.
    private const string PostCommentsRootsOnlyCteSqlBody = """
WITH PostCommentRoots AS (
    SELECT c.Id, c.Content, c.CreatedAt, c.ParentId, c.PostId, c.UserId, CAST(0 AS INTEGER) AS Level
    FROM Comments AS c
    WHERE c.PostId = {0} AND c.ParentId IS NULL
)
SELECT Id, Content, CreatedAt, ParentId, PostId, UserId, Level
FROM PostCommentRoots
""";

    // [2a][2b] GET /api/posts/{postId}/comments/* — SqlQueryRaw CTE độc lập theo postId + includeReplies (không gọi LoadRawCteAsync).
    public async Task<List<CommentCteDto>> GetAllCommentsForPost(
        Guid postId, // Id bài viết.
        bool includeReplies = true, // true: CTE đệ quy toàn cây trong bài; false: CTE chỉ các dòng gốc (Level 0).
        CancellationToken cancellationToken = default, // Hủy.
        SortByColumn? sort = null) // Thứ tự dòng: LINQ sau CTE.
    { // Mở khối GetAllCommentsForPost.
        // BƯỚC 1 — Chỉ thân CTE (đệ quy vs chỉ gốc), không mệnh đề ORDER BY trong chuỗi SQL.
        var sql = includeReplies ? PostCommentsRecursiveCteSqlBody : PostCommentsRootsOnlyCteSqlBody;
        // BƯỚC 2 — SqlQueryRaw + ToListAsync: một round-trip; sau đó ApplyUniversalSorting bằng LINQ (an toàn whitelist cột).
        var rows = await _context.Database // EF Database API cho raw SQL.
            .SqlQueryRaw<CommentCteDto>(sql, postId) // Thực thi CTE của riêng endpoint post/comments.
            .ToListAsync(cancellationToken); // Materialize danh sách phẳng có Level.
        var s = sort ?? CommentListSortDefault;
        return ApplyUniversalSorting(rows.AsQueryable(), s).ToList(); // Sắp theo query sort trên DTO.
    } // Kết thúc GetAllCommentsForPost.

    // [07] Route: DELETE /api/comments/{id}

    // [1][8][10][12] Danh sách phẳng phân trang (DbContext) — cặp với LoadFlatUnpagedAsync (cùng lọc, không Skip/Take).
    public async Task<(List<Comment> Items, long TotalCount)> LoadFlatAsync(
        Guid? postId, // null = toàn hệ; có giá trị = trong post.
        int page, // Số trang (1-based).
        int pageSize, // Số dòng mỗi trang.
        CancellationToken cancellationToken = default, // Hủy bất đồng bộ.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null, // Lọc CreatedAt.
        Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        string? contentContains = null, // null/rỗng = list thường; có giá trị = search theo content.
        SortByColumn? sort = null) // Thứ tự cột + hướng từ query.
    { // Mở khối LoadFlatAsync.
        // BƯỚC 1 — ApplyUniversalFilter: IQueryable đã lọc post/user/content/ngày (chưa SQL).
        var q = ApplyUniversalFilter(
            _context.Comments.AsNoTracking(),
            postId: postId,
            userId: userId,
            contentContains: contentContains,
            createdAtFrom: createdAtFrom,
            createdAtTo: createdAtTo); // Nguồn + post + user + content + khoảng thời gian.
        // BƯỚC 2 — LongCountAsync trên cùng q: tổng dòng khớp lọc (TotalCount / mẫu số TotalPages).
        var total = await q // Dùng lại cùng biểu thức IQueryable đã AsNoTracking.
            .LongCountAsync(cancellationToken); // COUNT(*) khớp lọc — mẫu số totalPages route phẳng.
        // BƯỚC 3 — Cùng q: sort LINQ → Skip → Take → ToListAsync (một trang entity phẳng).
        var s = sort ?? CommentListSortDefault;
        var items = await ApplyUniversalSorting(q, s) // OrderBy/ThenBy whitelist.
            .Skip((page - 1) * pageSize) // OFFSET.
            .Take(pageSize) // FETCH/LIMIT.
            .ToListAsync(cancellationToken); // Một trang entity Comment.
        return (items, total); // Trang + tổng cho API.
    } // Kết thúc LoadFlatAsync.

    // Dùng cho trạng thái Unpaged của một số route
    public async Task<List<Comment>> LoadFlatUnpagedAsync(
        Guid? postId = null, // null: mọi post; có giá trị: chỉ một bài.
        DateTime? createdAtFrom = null, // Lọc CreatedAt từ.
        DateTime? createdAtTo = null, // Lọc CreatedAt đến.
        Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        string? contentContains = null, // Tìm trong Content (tuỳ chọn).
        SortByColumn? sort = null, // Thứ tự hiển thị / tiền xử lý cây (mặc định CreatedAt tăng).
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối LoadFlatUnpagedAsync.
        // BƯỚC 1 — ApplyUniversalFilter: cùng lọc với LoadFlatAsync, không Skip/Take.
        var q = ApplyUniversalFilter(
            _context.Comments.AsNoTracking(),
            postId: postId,
            userId: userId,
            contentContains: contentContains,
            createdAtFrom: createdAtFrom,
            createdAtTo: createdAtTo); // IQueryable đã lọc post + user + content + ngày.
        // BƯỚC 2 — Mặc desc list default CreatedAt tăng khi sort null; nếu có sort thì dùng trực tiếp.
        var s = sort ?? CommentFlatUnpagedSortDefault;
        var ordered = ApplyUniversalSorting(q, s); // IOrderedQueryable trước ToListAsync.
        // BƯỚC 3 — ToListAsync: nạp toàn bộ hàng khớp lọc đã sắp (một hoặc vài round-trip tùy provider).
        return await ordered.ToListAsync(cancellationToken); // Một round-trip; có token thống nhất.
    } // Kết thúc LoadFlatUnpagedAsync.

    // Một câu SQL duy nhất: CTE đệ quy + lọc CreatedAt/UserId/Content (placeholder {0}..{4} do EF truyền tham số).
    private const string CommentFullCteSql = """
-- CTE CommentTree: duyệt toàn bộ cây comment theo quan hệ ParentId trong cùng PostId, gán Level = độ sâu (0 = gốc).
WITH CommentTree AS (
    -- Bước neo (anchor member): chọn mọi comment gốc (ParentId NULL); {0} NULL = mọi bài, có giá trị = chỉ PostId đó; Level khởi tạo 0.
    SELECT c.Id, c.Content, c.CreatedAt, c.ParentId, c.PostId, c.UserId, CAST(0 AS INTEGER) AS Level
    FROM Comments AS c
    WHERE c.ParentId IS NULL AND ({0} IS NULL OR c.PostId = {0})
    UNION ALL
    -- Bước đệ quy (recursive member): nối hàng con với hàng cha đã có trong CTE; buộc cùng PostId để không “nhảy” sang bài khác; Level = Level cha + 1.
    SELECT c2.Id, c2.Content, c2.CreatedAt, c2.ParentId, c2.PostId, c2.UserId, ct.Level + 1
    FROM Comments AS c2
    INNER JOIN CommentTree AS ct ON c2.ParentId = ct.Id AND c2.PostId = ct.PostId
)
-- Kết quả: mỗi dòng là một node đã đi qua cây; lọc theo khoảng CreatedAt [{1},{2}], UserId {3}, và Content LIKE mẫu {4} (NULL = bỏ điều kiện tương ứng).
SELECT Id, Content, CreatedAt, ParentId, PostId, UserId, Level
FROM CommentTree
WHERE ({1} IS NULL OR CreatedAt >= {1}) AND ({2} IS NULL OR CreatedAt <= {2})
  AND ({3} IS NULL OR UserId = {3})
  AND ({4} IS NULL OR Content LIKE {4})
""";
    // [09][11][13] Một round-trip SqlQueryRaw; phân trang theo gốc do CommentService (Skip/Take trên cây đã dựng).
    public async Task<List<CommentCteDto>> LoadRawCteAsync(
        Guid? postId, // Một post hoặc null = mọi gốc toàn hệ.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt từng dòng sau CTE.
        DateTime? createdAtTo = null, // Lọc CreatedAt từng dòng sau CTE.
        Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        string? contentContains = null, // LIKE %chuỗi% (tuỳ chọn).
        SortByColumn? sort = null) // Thứ tự dòng CTE: LINQ sau materialize.
    { // Mở khối LoadRawCteAsync: chuẩn bị tham số SQL rồi gọi CTE một lần.
        // BƯỚC 1 — Chuẩn hóa tham số {0}..{4}: DBNull.Value = vô hiệu điều kiện tương ứng trong WHERE của chuỗi SQL.
        object postArg = postId ?? (object)DBNull.Value; // {0}: NULL trong SQL nếu không lọc theo bài.
        object fromArg = createdAtFrom ?? (object)DBNull.Value; // {1}: cận dưới CreatedAt hoặc NULL.
        object toArg = createdAtTo ?? (object)DBNull.Value; // {2}: cận trên CreatedAt hoặc NULL.
        object userArg = userId ?? (object)DBNull.Value; // {3}: lọc UserId hoặc NULL.
        object likeArg = string.IsNullOrWhiteSpace(contentContains) // {4}: mẫu LIKE hoặc NULL.
            ? DBNull.Value // Không lọc nội dung → điều kiện LIKE bị vô hiệu trong WHERE.
            : (object)("%" + contentContains.Trim() + "%"); // Bọc %...% để tìm chuỗi con trong Content.
        // BƯỚC 2 — Chỉ thân CTE + WHERE (không mệnh đề ORDER BY trong raw SQL).
        var sql = CommentFullCteSql;
        // BƯỚC 3 — SqlQueryRaw materialize rồi ApplyUniversalSorting LINQ trên CommentCteDto (whitelist).
        var rows = await _context.Database // Truy cập low-level EF để SqlQueryRaw.
            .SqlQueryRaw<CommentCteDto>(sql, postArg, fromArg, toArg, userArg, likeArg) // Thực thi CTE với 5 placeholder an toàn.
            .ToListAsync(cancellationToken); // Materialize toàn bộ hàng phẳng.
        var s = sort ?? CommentListSortDefault;
        return ApplyUniversalSorting(rows.AsQueryable(), s).ToList(); // Sắp theo query an toàn.
    } // Kết thúc LoadRawCteAsync.

    // --- Demo loading (lazy / eager / explicit / projection) ---
    // Hợp đồng chung: cùng một CommentLoadingDemoDto — chỉ cần Post.Title, User.UserName, và số Children trực tiếp.
    // Không nạp Parent: DTO không hiển thị cha; nạp Parent ở một nhánh làm lệch số round-trip và mục tiêu so sánh.
    // Phân trang: cùng ApplyUniversalFilter, LongCount (khi paged), ApplyUniversalSorting, Skip/Take hoặc ToList — bốn demo đều nhìn thấy cùng một khung bước trong thân hàm.

    // [14] Route: GET /api/comments/demo/lazy-loading (mọi mode paginationEnabled).
    public async Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsLazyLoadingDemoRouteAsync( // Demo lazy một hàm cho mọi mode.
        bool paginationEnabled, // true = phân trang, false = lấy toàn bộ.
        int page, // Trang (chỉ dùng khi paginationEnabled=true).
        int pageSize, // Cỡ trang (chỉ dùng khi paginationEnabled=true).
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Lọc theo bài (tùy chọn).
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null, // Lọc CreatedAt.
        Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        string? contentContains = null, // Tìm trong Content (tuỳ chọn).
        SortByColumn? sort = null) // Thứ tự dòng demo: LINQ ApplyUniversalSorting.
    { // Mở khối GetCommentsLazyLoadingDemoRouteAsync.
        // BƯỚC 1 — ApplyUniversalFilter trên DbSet tracked (navigation có thể lazy sau khi materialize).
        var q = ApplyUniversalFilter(
            _context.Comments, // Tracked set (không AsNoTracking) để lazy sau này.
            postId: postId,
            userId: userId,
            contentContains: contentContains,
            createdAtFrom: createdAtFrom,
            createdAtTo: createdAtTo); // Thu hẹp theo postId (nếu có) + user + content + khoảng thời gian.
        // BƯỚC 2 — LongCount trên query đã lọc khi phân trang (trước materialize; chưa Include — cùng ý eager).
        var total = paginationEnabled
            ? await q.LongCountAsync(cancellationToken)
            : 0L;
        // BƯỚC 3 — ApplyUniversalSorting trên entity (chưa Include; thứ tự dòng trước khi nạp trang).
        var s = sort ?? CommentListSortDefault;
        var ordered = ApplyUniversalSorting(q, s);
        // BƯỚC 4 — Skip/Take một trang hoặc ToList toàn bộ đã sắp.
        var rows = paginationEnabled
            ? await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken)
            : await ordered.ToListAsync(cancellationToken);

        // BƯỚC 5 — Duyệt từng comment: đọc Post?.Title, User?.UserName, Children.Count (có thể phát sinh thêm SQL lazy).
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

        if (!paginationEnabled)
            total = list.Count;

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
        DateTime? createdAtTo = null, // Lọc CreatedAt.
        Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        string? contentContains = null, // Tìm trong Content (tuỳ chọn).
        SortByColumn? sort = null) // Sort LINQ trên entity sau Include.
    { // Mở khối GetCommentsEagerLoadingDemoRouteAsync.
        // BƯỚC 1 — ApplyUniversalFilter trên Comments.AsNoTracking: query đã lọc, chưa Include.
        var baseQuery = ApplyUniversalFilter(
            _context.Comments // DbSet.
                .AsNoTracking(), // Không track.
            postId: postId,
            userId: userId,
            contentContains: contentContains,
            createdAtFrom: createdAtFrom,
            createdAtTo: createdAtTo); // Thu hẹp theo postId (nếu có) + user + content + khoảng thời gian.
        // BƯỚC 2 — LongCount trên baseQuery khi paged (trước Include) để tổng khớp filter không bị sai do join.
        var total = paginationEnabled // Nếu phân trang thì đếm trước trên query chưa Include (tránh đếm sai do join).
            ? await baseQuery.LongCountAsync(cancellationToken) // Tổng dòng khớp filter thuần.
            : 0L; // Unpaged: tạm 0; sẽ gán = list.Count sau khi nạp.
        // BƯỚC 3 — Include Post/User/Children + AsSplitQuery rồi ApplyUniversalSorting trước Skip/Take.
        var withNav = baseQuery // Query đã lọc.
            .Include(c => c.Post) // Nạp Post.
            .Include(c => c.User) // Nạp User.
            .Include(c => c.Children) // Nạp Children.
            .AsSplitQuery(); // Tách query.
        var s = sort ?? CommentListSortDefault;
        var query = ApplyUniversalSorting(withNav, s); // Sort theo cột entity sau Include (EF dịch ổn định).
        // BƯỚC 4 — Skip/Take một trang hoặc ToList toàn bộ; unpaged sẽ gán total = list.Count ở bước sau.
        var rows = paginationEnabled // Chọn nhánh materialize.
            ? await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken) // Một trang sau Include.
            : await query.ToListAsync(cancellationToken); // Toàn bộ sau Include (nhiều round-trip split).

        // BƯỚC 5 — ConvertAll sang CommentLoadingDemoDto trong RAM (không SQL thêm).
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
        DateTime? createdAtTo = null, // Lọc CreatedAt.
        Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        string? contentContains = null, // Tìm trong Content (tuỳ chọn).
        SortByColumn? sort = null) // Sort LINQ trên entity.
    { // Mở khối GetCommentsExplicitLoadingDemoRouteAsync.
        // BƯỚC 1 — ApplyUniversalFilter trên tracked (cùng phạm vi lọc với lazy).
        var q = ApplyUniversalFilter(
            _context.Comments, // Tracked.
            postId: postId,
            userId: userId,
            contentContains: contentContains,
            createdAtFrom: createdAtFrom,
            createdAtTo: createdAtTo); // Cùng phạm vi với lazy demo.
        // BƯỚC 2 — LongCount khi phân trang (query đã lọc, chưa Load navigation — giống lazy).
        var total = paginationEnabled
            ? await q.LongCountAsync(cancellationToken)
            : 0L;
        // BƯỚC 3 — Sort whitelist trên entity trước khi materialize trang.
        var s = sort ?? CommentListSortDefault;
        var ordered = ApplyUniversalSorting(q, s);
        // BƯỚC 4 — Một trang hoặc toàn bộ entity đã sắp (sau đó mới explicit LoadAsync từng quan hệ).
        var rows = paginationEnabled
            ? await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken)
            : await ordered.ToListAsync(cancellationToken);

        // BƯỚC 5 — Map sang DTO: duyệt từng entity đã sắp; trong vòng lặp có LoadAsync navigation (5a) và Add DTO (5b).
        var list = new List<CommentLoadingDemoDto>(rows.Count); // Danh sách đích.
        foreach (var comment in rows) // Từng dòng trang.
        { // Mở khối.
            // 5a — Ba lần LoadAsync (Post, User, Children) — explicit loading, tách round-trip.
            await _context.Entry(comment).Reference(c => c.Post).LoadAsync(cancellationToken); // SQL: nạp Post.
            await _context.Entry(comment).Reference(c => c.User).LoadAsync(cancellationToken); // SQL: nạp User.
            await _context.Entry(comment).Collection(c => c.Children).LoadAsync(cancellationToken); // SQL: nạp Children.

            // 5b — Thêm CommentLoadingDemoDto (nhãn "explicit") sau khi đã nạp đủ quan hệ.
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

        if (!paginationEnabled)
            total = list.Count;

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
        DateTime? createdAtTo = null, // Lọc CreatedAt.
        Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        string? contentContains = null, // Tìm trong Content (tuỳ chọn).
        SortByColumn? sort = null) // Sort sau projection DTO.
    { // Mở khối GetCommentsProjectionDemoRouteAsync.
        // BƯỚC 1 — ApplyUniversalFilter + AsNoTracking: IQueryable đã lọc, chưa projection.
        var q = ApplyUniversalFilter(
            _context.Comments // DbSet.
                .AsNoTracking(), // Không track.
            postId: postId,
            userId: userId,
            contentContains: contentContains,
            createdAtFrom: createdAtFrom,
            createdAtTo: createdAtTo); // Filter một bài nếu có postId + user + content + khoảng thời gian.
        // BƯỚC 2 — LongCount trên q khi paged (trước Select projection).
        var total = paginationEnabled // Đếm trước khi Select nếu paged.
            ? await q.LongCountAsync(cancellationToken) // Tổng khớp filter trên entity.
            : 0L; // Unpaged: gán sau từ items.Count.
        // BƯỚC 3 — Select CommentLoadingDemoDto rồi ApplyUniversalSorting trên IQueryable DTO (đúng cột JSON trả về).
        var s = sort ?? CommentListSortDefault;
        var query = q.Select(c => new CommentLoadingDemoDto // Projection + đếm con trên SQL.
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
            });
        query = ApplyUniversalSorting(query, s); // OrderBy trên DTO sau Select.
        // BƯỚC 4 — Skip/Take một trang hoặc ToList toàn bộ DTO; unpaged gán total = items.Count.
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
        // BƯỚC 1 — Posts.AsNoTracking + AnyAsync theo Id: EXISTS nhanh, không nạp entity đầy đủ.
        return await _context.Posts // DbSet Posts.
            .AsNoTracking() // Chỉ đọc.
            .AnyAsync(x => x.Id == postId); // Kiểm tra một Id post tồn tại.
    } // Kết thúc PostExistsAsync.

    // Helper support: [03][04][06] validate userId.
    public async Task<bool> UserExistsAsync(Guid userId) // Kiểm tra người dùng tồn tại.
    { // Mở khối UserExistsAsync.
        // BƯỚC 1 — Users.AsNoTracking + AnyAsync theo Id user.
        return await _context.Users // DbSet Users (Identity).
            .AsNoTracking() // Chỉ đọc.
            .AnyAsync(x => x.Id == userId); // Kiểm tra user tồn tại.
    } // Kết thúc UserExistsAsync.

    // Helper support: [04][06] validate parent comment cùng post.
    public async Task<bool> ParentExistsAsync(Guid parentId, Guid postId) // Cha phải cùng post.
    { // Mở khối ParentExistsAsync.
        // BƯỚC 1 — Comments.AsNoTracking + Any: tồn tại comment Id == parentId cùng PostId == postId.
        return await _context.Comments // DbSet Comments.
            .AsNoTracking() // Chỉ đọc.
            .AnyAsync(x => x.Id == parentId && x.PostId == postId); // Cha phải cùng post với con.
    } // Kết thúc ParentExistsAsync.

    // Universal filter duy nhất cho Comment: gom điều kiện route vào một pipeline IQueryable.
    // Mục đích: gom mọi điều kiện lọc route Comment vào một IQueryable — không thực thi SQL; thứ tự Where tương đương AND.
    private static IQueryable<Comment> ApplyUniversalFilter(
        IQueryable<Comment> query,
        Guid? postId = null,
        Guid? userId = null,
        string? contentContains = null,
        Guid? parentId = null,
        bool? isRoot = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null)
    {
        // BƯỚC 1 — Lọc theo bài viết (tuỳ chọn).
        // TRƯỜNG HỢP: postId có giá trị → chỉ comment thuộc PostId đó.
        if (postId is { } pid)
        {
            query = query.Where(c => c.PostId == pid);
        }

        // BƯỚC 2 — Lọc theo tác giả (tuỳ chọn).
        // TRƯỜNG HỢP: userId có giá trị → chỉ comment do user đó viết.
        if (userId is { } uid)
        {
            query = query.Where(c => c.UserId == uid);
        }

        // BƯỚC 3 — Tìm theo nội dung (tuỳ chọn).
        // TRƯỜNG HỢP: chuỗi null hoặc chỉ khoảng trắng → bỏ qua; ngược lại → Contains (EF dịch sang SQL LIKE tùm provider).
        if (!string.IsNullOrWhiteSpace(contentContains))
        {
            query = query.Where(c => c.Content.Contains(contentContains));
        }

        // BƯỚC 4 — Lọc con trực tiếp của một cha (tuỳ chọn).
        if (parentId is { } parent)
        {
            query = query.Where(c => c.ParentId == parent);
        }

        // BƯỚC 5 — Lọc gốc / không gốc (tuỳ chọn).
        // TRƯỜNG HỢP A: isRoot == true → ParentId == null (thread gốc).
        // TRƯỜNG HỢP B: isRoot == false → ParentId != null (chỉ reply).
        if (isRoot is { } root)
        {
            query = root
                ? query.Where(c => c.ParentId == null)
                : query.Where(c => c.ParentId != null);
        }

        // BƯỚC 6 — Khoảng CreatedAt inclusive qua RepositoryBase (EF.Property CreatedAt).
        query = ApplyCreatedAtRange(query, createdAtFrom, createdAtTo);

        return query;
    }

    #endregion
} // Kết thúc lớp CommentRepository.
