using CommentAPI.DTOs;

// Hợp đồng dịch vụ: thứ tự thành viên bám CommentsController (GET → CRUD → flat/cte/tree → demo danh sách), rồi bổ trợ không map route trực tiếp.

namespace CommentAPI.Interfaces;

// Giao diện service Comment: CRUD, tìm kiếm, cây phẳng/CTE, demo EF loading; implement gọi repository + mapping.
public interface ICommentService
{
    // GET /api/comments: lọc postId, content; khoảng CreatedAt inclusive; unpaged khi caller bật (vd. demo).
    Task<PagedResult<CommentDto>> GetCommentListAsync(
        Guid? postId,
        string? contentContains,
        bool unpaged,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // GET /api/comments/{id}
    Task<CommentDto> GetByIdAsync(Guid id);

    // GET /api/comments/user/{userId}
    Task<PagedResult<CommentDto>> GetCommentsByUserIdPagedAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<CommentDto> CreateAsync(CreateCommentDto dto);

    Task UpdateAsAuthorAsync(Guid id, UpdateCommentDto dto, Guid currentUserId);

    Task UpdateAsAdminAsync(Guid id, AdminUpdateCommentDto dto);

    Task DeleteAsync(Guid id);

    // GET /api/comments/flat — một hàm xử lý cả postId/null.
    Task<PagedResult<CommentDto>> GetFlatRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // GET /api/comments/cte — một hàm xử lý cả postId/null.
    Task<PagedResult<CommentFlatDto>> GetCteFlatRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // GET /api/comments/tree/flat — một hàm xử lý cả postId/null.
    Task<PagedResult<CommentTreeFlatDto>> GetTreeFlatRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // GET /api/comments/tree/cte — một hàm xử lý cả postId/null.
    Task<PagedResult<CommentTreeDto>> GetTreeCteRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // GET /api/comments/tree/flat/flatten — một hàm xử lý cả postId/null.
    Task<PagedResult<CommentFlatNoLevelDto>> GetTreeFlatFlattenRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // GET /api/comments/tree/cte/flatten — một hàm xử lý cả postId/null.
    Task<PagedResult<CommentFlatDto>> GetTreeCteFlattenRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // Demo danh sách: GET /api/comments/demo/* (lazy, eager, explicit, projection) — unpaged + paged.
    Task<PagedResult<CommentLoadingDemoDto>> GetCommentsLazyLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<PagedResult<CommentLoadingDemoDto>> GetCommentsEagerLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<PagedResult<CommentLoadingDemoDto>> GetCommentsExplicitLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<PagedResult<CommentLoadingDemoDto>> GetCommentsProjectionDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<IReadOnlyList<CommentLoadingDemoDto>> GetAllCommentsLazyLoadingDemoAsync(
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<IReadOnlyList<CommentLoadingDemoDto>> GetAllCommentsEagerLoadingDemoAsync(
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<IReadOnlyList<CommentLoadingDemoDto>> GetAllCommentsExplicitLoadingDemoAsync(
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<IReadOnlyList<CommentLoadingDemoDto>> GetAllCommentsProjectionDemoAsync(
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

}
