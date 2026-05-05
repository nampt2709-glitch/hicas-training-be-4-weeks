using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Entities;

namespace CommentAPI.Interfaces;

// Hợp đồng truy cập dữ liệu Comment: CRUD, phân trang, CTE, demo loading — mỗi tham số dòng kèm chú thích vai trò.
public interface ICommentRepository
{
    // Parse sort/sortDir query; unpagedFlatDefaultsToCreatedAt=true cho LoadFlatUnpagedAsync (mặc định CreatedAt tăng).
    SortByColumn ParseCommentListSortOrThrow(string? sort, string? sortDir, bool unpagedFlatDefaultsToCreatedAt = false);

    // Đếm mọi comment khớp lọc (dùng làm TotalComments / so khớp với LoadFlatAsync).
    Task<long> CountCommentsMatchingRouteAsync(
        Guid? postId, // null = mọi bài; có giá trị = chỉ trong một bài.
        string? contentContains, // null/blank = không lọc nội dung; có = Contains trên Content.
        CancellationToken cancellationToken = default, // Hủy truy vấn bất đồng bộ.
        DateTime? createdAtFrom = null, // Cận dưới CreatedAt (inclusive) hoặc bỏ.
        DateTime? createdAtTo = null, // Cận trên CreatedAt (inclusive) hoặc bỏ.
        Guid? userId = null); // Lọc tác giả hoặc null = mọi user.

    // Đếm comment gốc (ParentId == null) khớp lọc — mẫu số phân trang theo “số thread”.
    Task<long> CountCommentRootsMatchingRouteAsync(
        Guid? postId, // Phạm vi bài hoặc toàn hệ.
        string? contentContains, // Lọc nội dung tùy chọn.
        CancellationToken cancellationToken = default, // Token hủy.
        DateTime? createdAtFrom = null, // Lọc ngày từ.
        DateTime? createdAtTo = null, // Lọc ngày đến.
        Guid? userId = null); // Lọc tác giả.

    // Một trang các gốc (ParentId null) đã sắp; tổng số gốc khớp lọc kèm trong tuple.
    Task<(List<Comment> Items, long TotalRootCount)> GetCommentRootsRoutePagedAsync(
        Guid? postId, // Lọc theo bài hoặc null.
        string? contentContains, // Tìm trong Content hoặc bỏ.
        int page, // Trang 1-based trên tập gốc.
        int pageSize, // Số gốc mỗi trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null, // Lọc CreatedAt.
        Guid? userId = null, // Lọc UserId.
        SortByColumn? sort = null); // Thứ tự gốc (cột + hướng).

    // Nạp mọi comment thuộc subtree của từng rootId (BFS theo tầng, cùng PostId trong DB).
    Task<List<Comment>> LoadCommentsForSubtreesAsync(
        IReadOnlyList<Guid> rootIds, // Danh sách Id gốc cần mở rộng con cháu.
        CancellationToken cancellationToken = default, // Hủy.
        SortByColumn? sort = null); // Thứ tự kết quả phẳng sau BFS.

    // Danh sách phẳng không phân trang, cùng bộ lọc với LoadFlatAsync; sort do tham số sort quy định.
    Task<List<Comment>> LoadFlatUnpagedAsync(
        Guid? postId = null, // Phạm vi một bài hoặc toàn hệ.
        DateTime? createdAtFrom = null, // Khoảng ngày.
        DateTime? createdAtTo = null, // Khoảng ngày.
        Guid? userId = null, // Tác giả.
        string? contentContains = null, // Chuỗi Contains.
        SortByColumn? sort = null, // Kiểu sắp xếp kết quả (null → CreatedAt tăng).
        CancellationToken cancellationToken = default); // Hủy.

    // Phân trang phẳng: COUNT + một trang entity Comment khớp lọc.
    Task<(List<Comment> Items, long TotalCount)> LoadFlatAsync(
        Guid? postId, // null = toàn hệ; Guid = một bài.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc ngày.
        DateTime? createdAtTo = null, // Lọc ngày.
        Guid? userId = null, // Lọc user.
        string? contentContains = null, // Lọc nội dung.
        SortByColumn? sort = null); // Thứ tự dòng.

    // Một câu SqlQueryRaw CTE: mọi dòng phẳng có Level (service phân trang gốc trong RAM); kiểu CommentCteDto (route GET …/cte / post …/flat).
    Task<List<CommentCteDto>> LoadRawCteAsync(
        Guid? postId, // null = mọi bài trong CTE anchor.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Truyền vào WHERE sau CTE.
        DateTime? createdAtTo = null, // Truyền vào WHERE sau CTE.
        Guid? userId = null, // Lọc UserId trong SQL.
        string? contentContains = null, // LIKE %term% trong SQL.
        SortByColumn? sort = null); // LINQ sort sau materialize.

    // Đọc một comment chiếu thẳng CommentDto; postId tùy chọn để siết phạm vi một bài.
    Task<CommentDto?> GetCommentByIdRouteReadAsync(
        Guid id, // Khóa comment.
        Guid? postId = null, // Nếu có: thêm điều kiện PostId khớp.
        CancellationToken cancellationToken = default); // Hủy.

    // Nạp tracked toàn comment của một post — phục vụ admin đổi PostId hàng loạt subtree.
    Task<List<Comment>> GetCommentsByPostTrackedForAdminRouteAsync(
        Guid postId, // Id bài viết.
        CancellationToken cancellationToken = default); // Hủy.

    // Một bài: SqlQueryRaw với CTE riêng (không dùng LoadRawCteAsync). true = CTE đệ quy gốc→con trong PostId + Level; false = CTE chỉ lớp gốc (Level 0).
    Task<List<CommentCteDto>> GetAllCommentsForPost(
        Guid postId, // Bài cần nạp comment (bắt buộc; không hỗ trợ null toàn hệ).
        bool includeReplies = true, // true = toàn thread; false = chỉ comment ParentId null của bài đó.
        CancellationToken cancellationToken = default, // Hủy truy vấn bất đồng bộ.
        SortByColumn? sort = null); // LINQ sort sau materialize.

    // Lấy entity theo Id (tracked path qua RepositoryBase); null nếu không có.
    Task<Comment?> GetByIdAsync(Guid id); // Khóa Guid.

    // Đánh dấu thêm entity vào DbContext (chưa flush cho đến SaveChanges).
    Task AddAsync(Comment comment); // Entity mới.

    // Đánh dấu entity đã track là Modified.
    void Update(Comment comment); // Entity cần cập nhật.

    // Đánh dấu xóa entity khỏi context (hoặc soft-delete tùy mapping).
    void Remove(Comment comment); // Entity cần xóa.

    // Kiểm tra tồn tại bài viết theo Id.
    Task<bool> PostExistsAsync(Guid postId); // Khóa post.

    // Kiểm tra tồn tại user theo Id.
    Task<bool> UserExistsAsync(Guid userId); // Khóa user.

    // Cha phải tồn tại và cùng PostId với comment con dự kiến.
    Task<bool> ParentExistsAsync(Guid parentId, Guid postId); // Id cha và Id bài.

    // Demo lazy: tracked query + truy cập navigation có thể phát sinh SQL thêm.
    Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsLazyLoadingDemoRouteAsync(
        bool paginationEnabled, // true = Skip/Take; false = ToList toàn bộ khớp lọc.
        int page, // Trang khi paginationEnabled.
        int pageSize, // Cỡ trang khi paginationEnabled.
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Lọc bài.
        DateTime? createdAtFrom = null, // Lọc ngày.
        DateTime? createdAtTo = null, // Lọc ngày.
        Guid? userId = null, // Lọc tác giả.
        string? contentContains = null, // Lọc Content.
        SortByColumn? sort = null); // Sort.

    // Demo eager: Include Post/User/Children + AsSplitQuery.
    Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsEagerLoadingDemoRouteAsync(
        bool paginationEnabled, // Bật/tắt phân trang.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Lọc post.
        DateTime? createdAtFrom = null, // Ngày.
        DateTime? createdAtTo = null, // Ngày.
        Guid? userId = null, // User.
        string? contentContains = null, // Content.
        SortByColumn? sort = null); // Sort.

    // Demo explicit: sau khi materialize comment, gọi LoadAsync từng navigation.
    Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsExplicitLoadingDemoRouteAsync(
        bool paginationEnabled, // Phân trang hay không.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Post.
        DateTime? createdAtFrom = null, // Từ.
        DateTime? createdAtTo = null, // Đến.
        Guid? userId = null, // User.
        string? contentContains = null, // Content.
        SortByColumn? sort = null); // Sort.

    // Demo projection: Select CommentLoadingDemoDto trên server, không Include đồ thị đầy đủ.
    Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsProjectionDemoRouteAsync(
        bool paginationEnabled, // Phân trang.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Post.
        DateTime? createdAtFrom = null, // Ngày.
        DateTime? createdAtTo = null, // Ngày.
        Guid? userId = null, // User.
        string? contentContains = null, // Content.
        SortByColumn? sort = null); // Sort.

    // Flush mọi thay đổi đang treo xuống SQL.
    Task SaveChangesAsync();
}
