using CommentAPI.DTOs;

namespace CommentAPI.Interfaces;

public interface ICommentService
{
    Task<PagedResult<CommentDto>> GetAllPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<CommentDto> GetByIdAsync(Guid id);
    Task<CommentDto> CreateAsync(CreateCommentDto dto);
    Task UpdateAsync(Guid id, UpdateCommentDto dto);
    Task DeleteAsync(Guid id);

    /// <summary>Tìm comment theo nội dung (chuỗi chứa), có phân trang.</summary>
    Task<PagedResult<CommentDto>> SearchByContentPagedAsync(
        string? content,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Tìm đúng một comment theo id trong phạm vi một post.</summary>
    Task<CommentDto> GetByIdInPostAsync(Guid postId, Guid commentId, CancellationToken cancellationToken = default);

    /// <summary>Tìm theo nội dung trong một post (phân trang).</summary>
    Task<PagedResult<CommentDto>> SearchByContentInPostPagedAsync(
        Guid postId,
        string? content,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResult<CommentDto>> GetAllFlatPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResult<CommentTreeDto>> GetAllTreePagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResult<CommentFlatDto>> GetAllCteFlatPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResult<CommentTreeDto>> GetAllCteTreePagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Cây EF (trang gốc) làm phẳng preorder — không dùng CTE.</summary>
    Task<PagedResult<CommentFlatDto>> GetFlattenedForestPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Toàn cục: CTE mọi post → cây → danh sách phẳng, rồi phân trang theo dòng phẳng.</summary>
    Task<PagedResult<CommentFlatDto>> GetFlattenedFromCtePagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResult<CommentDto>> GetFlatByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResult<CommentFlatDto>> GetCteFlatByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResult<CommentTreeDto>> GetTreeByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResult<CommentTreeDto>> GetCteTreeByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResult<CommentFlatDto>> GetFlattenedTreeByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Cây từ CTE theo post rồi làm phẳng preorder (Level theo độ sâu DFS).</summary>
    Task<PagedResult<CommentFlatDto>> GetFlattenedCteTreeByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Route demo: lazy loading trên <see cref="Entities.Comment"/>.</summary>
    Task<CommentLoadingDemoDto> GetCommentLazyLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Route demo: eager loading (Include).</summary>
    Task<CommentLoadingDemoDto> GetCommentEagerLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Route demo: explicit loading (Entry LoadAsync).</summary>
    Task<CommentLoadingDemoDto> GetCommentExplicitLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Route demo: projection (Select SQL) một comment.</summary>
    Task<CommentLoadingDemoDto> GetCommentProjectionDemoAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<CommentLoadingDemoDto>> GetCommentsLazyLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResult<CommentLoadingDemoDto>> GetCommentsEagerLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResult<CommentLoadingDemoDto>> GetCommentsExplicitLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResult<CommentLoadingDemoDto>> GetCommentsProjectionDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
