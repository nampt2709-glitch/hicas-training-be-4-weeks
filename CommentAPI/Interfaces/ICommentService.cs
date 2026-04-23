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

    // GET /api/comments/flat — có postId trước, toàn hệ sau (như nhánh if trong controller).
    Task<PagedResult<CommentDto>> GetFlatByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<PagedResult<CommentDto>> GetAllFlatPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // GET /api/comments/cte
    Task<PagedResult<CommentFlatDto>> GetCteFlatByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<PagedResult<CommentFlatDto>> GetAllCteFlatPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // GET /api/comments/tree/flat
    Task<PagedResult<CommentTreeDto>> GetTreeByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<PagedResult<CommentTreeDto>> GetAllTreePagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // GET /api/comments/tree/cte
    Task<PagedResult<CommentTreeDto>> GetCteTreeByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<PagedResult<CommentTreeDto>> GetAllCteTreePagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // GET /api/comments/tree/flat/flatten
    Task<PagedResult<CommentFlatDto>> GetFlattenedTreeByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<PagedResult<CommentFlatDto>> GetFlattenedForestPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    // GET /api/comments/tree/cte/flatten
    Task<PagedResult<CommentFlatDto>> GetFlattenedCteTreeByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<PagedResult<CommentFlatDto>> GetFlattenedFromCtePagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
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

    // Bổ trợ: phân trang toàn hệ, tìm kiếm, đọc theo post; demo một Id (không có route trong CommentsController).
    Task<PagedResult<CommentDto>> GetAllPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<IReadOnlyList<CommentDto>> GetAllByPostIdAsync(
        Guid postId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<CommentDto>> SearchByContentPagedAsync(
        string? content,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<CommentDto> GetByIdInPostAsync(Guid postId, Guid commentId, CancellationToken cancellationToken = default);

    Task<PagedResult<CommentDto>> SearchByContentInPostPagedAsync(
        Guid postId,
        string? content,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null);

    Task<CommentLoadingDemoDto> GetCommentLazyLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CommentLoadingDemoDto> GetCommentEagerLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CommentLoadingDemoDto> GetCommentExplicitLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CommentLoadingDemoDto> GetCommentProjectionDemoAsync(Guid id, CancellationToken cancellationToken = default);
}
