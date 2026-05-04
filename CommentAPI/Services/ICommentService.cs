using CommentAPI; // CommentRouteListSort cho query sort dropdown.
using CommentAPI.DTOs;

// Hợp đồng dịch vụ: thứ tự thành viên bám CommentsController (GET → CRUD → flat/cte/tree → demo danh sách), rồi bổ trợ không map route trực tiếp.

namespace CommentAPI.Interfaces;

// Giao diện service Comment: CRUD, tìm kiếm, danh sách phẳng / CTE / cây, demo kiểu nạp quan hệ; gọi repository + mapping.
public interface ICommentService
{
    // GET /api/comments: lọc postId, userId, content; khoảng CreatedAt inclusive; unpaged khi caller bật (vd. demo).
    Task<PagedResult<CommentDto>> GetCommentListAsync(
        Guid? postId,
        string? contentContains,
        bool unpaged,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId);

    // GET /api/comments/{id}
    Task<CommentDto> GetByIdAsync(Guid id);

    // GET /api/comments/user/{userId}
    Task<PagedResult<CommentDto>> GetCommentsByUserIdPagedAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        string? contentContains = null,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId);

    Task<CommentDto> CreateAsync(CreateCommentDto dto);

    Task UpdateAsAuthorAsync(Guid id, UpdateCommentDto dto, Guid currentUserId);

    Task UpdateAsAdminAsync(Guid id, AdminUpdateCommentDto dto);

    Task DeleteAsync(Guid id);

    // GET /api/comments/flat — một hàm xử lý cả postId/null; payload CommentFlatDto (EF, không CTE).
    Task<PagedResult<CommentFlatDto>> GetFlatRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId);

    // GET /api/comments/cte — một hàm xử lý cả postId/null; payload CommentCteDto (CTE + preorder).
    Task<PagedResult<CommentCteDto>> GetCteFlatRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId);

    // GET /api/comments/tree/flat — một hàm xử lý cả postId/null.
    Task<PagedResult<CommentTreeFlatDto>> GetTreeFlatRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId);

    // GET /api/comments/tree/cte — một hàm xử lý cả postId/null; payload CommentTreeCteDto.
    Task<PagedResult<CommentTreeCteDto>> GetTreeCteRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId);

    // GET /api/comments/tree/flat/flatten — một hàm xử lý cả postId/null; payload CommentFlattenFlatDto.
    Task<PagedResult<CommentFlattenFlatDto>> GetTreeFlatFlattenRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId);

    // GET /api/comments/tree/cte/flatten — một hàm xử lý cả postId/null; payload CommentFlattenCteDto.
    Task<PagedResult<CommentFlattenCteDto>> GetTreeCteFlattenRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId);

    // GET /api/posts/{postId}/comments/tree — CommentTreeCteDto (CTE repo + BuildTreeCte).
    Task<IReadOnlyList<CommentTreeCteDto>> GetCommentsTreeForPostAsync(
        Guid postId,
        bool includeReplies = true,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId,
        CancellationToken cancellationToken = default);

    // GET /api/posts/{postId}/comments/flat — CommentCteDto (CTE repo, không BuildTreeCte).
    Task<IReadOnlyList<CommentCteDto>> GetCommentsFlatForPostAsync(
        Guid postId,
        bool includeReplies = true,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId,
        CancellationToken cancellationToken = default);

    // Demo danh sách: GET /api/comments/demo/* (lazy, eager, explicit, projection) — unpaged + paged.
    Task<PagedResult<CommentLoadingDemoDto>> GetCommentsLazyLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId);

    Task<PagedResult<CommentLoadingDemoDto>> GetCommentsEagerLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId);

    Task<PagedResult<CommentLoadingDemoDto>> GetCommentsExplicitLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId);

    Task<PagedResult<CommentLoadingDemoDto>> GetCommentsProjectionDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId);

    Task<IReadOnlyList<CommentLoadingDemoDto>> GetAllCommentsLazyLoadingDemoAsync(
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId);

    Task<IReadOnlyList<CommentLoadingDemoDto>> GetAllCommentsEagerLoadingDemoAsync(
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId);

    Task<IReadOnlyList<CommentLoadingDemoDto>> GetAllCommentsExplicitLoadingDemoAsync(
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId);

    Task<IReadOnlyList<CommentLoadingDemoDto>> GetAllCommentsProjectionDemoAsync(
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        CommentRouteListSort sort = CommentRouteListSort.ByPostCreatedAtId);

}
