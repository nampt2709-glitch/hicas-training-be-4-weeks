using CommentAPI.DTOs;

using CommentAPI.Entities;



// Không gian tên tập hợp hợp đồng (interface) tầng dữ liệu.

namespace CommentAPI.Interfaces;



// Hợp đồng truy cập dữ liệu Comment: CRUD, phân trang, CTE, demo loading, phục vụ service.

public interface ICommentRepository

{
    // Route group — /api/comments (list, flat, search)
    Task<(List<Comment> Items, long TotalCount)> GetCommentsRoutePagedAsync(
        Guid? postId,
        string? contentContains,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<(List<Comment> Items, long TotalCount)> GetCommentsByUserRoutePagedAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // Dữ liệu thô EF flat/tree: [08] GET /api/comments/flat, [10] GET /api/comments/tree/flat, [12] GET /api/comments/tree/flat/flatten (rootsOnly + loadCommentsForRootPosts).
    Task<(List<Comment> Items, long TotalCount, List<Comment> RelatedComments)> LoadRawFlatAsync(
        Guid? postId,
        int page,
        int pageSize,
        bool rootsOnly,
        bool loadCommentsForRootPosts,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<List<Comment>> SearchCommentsRouteAllAsync(
        Guid? postId,
        string contentContains,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<List<Comment>> GetCommentsRouteAllAsync(
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // Dữ liệu thô CTE (hàng phẳng có Level): [09] GET /api/comments/cte, [11] GET /api/comments/tree/cte, [13] GET /api/comments/tree/cte/flatten.
    Task<List<CommentFlatDto>> LoadRawCteAsync(
        Guid? postId,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // Route group — /api/comments/{id}
    Task<CommentDto?> GetCommentByIdRouteReadAsync(Guid id, Guid? postId = null, CancellationToken cancellationToken = default);

    // Tracked query phục vụ route admin update khi chuyển post cho cả subtree.
    Task<List<Comment>> GetCommentsByPostTrackedForAdminRouteAsync(

        Guid postId,

        CancellationToken cancellationToken = default);



    // Lấy entity theo id — có thể trả null nếu không tồn tại; thường dùng khi cần cập nhật/xoá.

    Task<Comment?> GetByIdAsync(Guid id);

    // Thêm comment mới (chưa ghi DB nếu chưa gọi SaveChanges).

    Task AddAsync(Comment comment);

    // Đánh dấu thay đổi trên entity đã theo dõi.

    void Update(Comment comment);

    // Đánh dấu xoá (hoặc xoá mềm tùy cấu hình) trên context.

    void Remove(Comment comment);

    // Kiểm tra post tồn tại (khóa ngoại khi tạo comment).

    Task<bool> PostExistsAsync(Guid postId);

    // Kiểm tra user tồn tại (gán Author).

    Task<bool> UserExistsAsync(Guid userId);

    // Kiểm tra bình luận cha thuộc cùng post (tránh liên kết chéo post).

    Task<bool> ParentExistsAsync(Guid parentId, Guid postId);

    // Route demo lazy: một hàm xử lý cả paginationEnabled=true/false.
    Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsLazyLoadingDemoRouteAsync(
        bool paginationEnabled,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // Route demo eager: một hàm xử lý cả paginationEnabled=true/false.
    Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsEagerLoadingDemoRouteAsync(
        bool paginationEnabled,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // Route demo explicit: một hàm xử lý cả paginationEnabled=true/false.
    Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsExplicitLoadingDemoRouteAsync(
        bool paginationEnabled,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // Route demo projection: một hàm xử lý cả paginationEnabled=true/false.
    Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsProjectionDemoRouteAsync(
        bool paginationEnabled,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);



    // Lưu mọi thay đổi đang treo (insert/update/delete) xuống cơ sở dữ liệu.

    Task SaveChangesAsync();

}


