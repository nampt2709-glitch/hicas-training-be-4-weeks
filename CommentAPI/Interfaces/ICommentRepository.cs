using CommentAPI.DTOs;
using CommentAPI.Entities;

namespace CommentAPI.Interfaces;

public interface ICommentRepository
{
    Task<List<Comment>> GetAllAsync();

    Task<(List<Comment> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<(List<Comment> Items, long TotalCount)> GetByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<(List<Comment> Items, long TotalCount)> GetRootCommentsPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<(List<Comment> Items, long TotalCount)> GetRootsByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Tìm comment có <see cref="Comment.Content"/> chứa chuỗi (phân trang).</summary>
    Task<(List<Comment> Items, long TotalCount)> SearchByContentPagedAsync(
        string contentContains,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<List<Comment>> GetCommentsForPostsAsync(
        IReadOnlyCollection<Guid> postIds,
        CancellationToken cancellationToken = default);

    Task<List<Comment>> GetByPostIdAsync(Guid postId);

    /// <summary>Đọc GET theo id — projection <see cref="CommentDto"/>, AsNoTracking.</summary>
    Task<CommentDto?> GetByIdForReadAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Đọc comment theo id trong một post (projection).</summary>
    Task<CommentDto?> GetByIdForReadInPostAsync(Guid postId, Guid commentId, CancellationToken cancellationToken = default);

    /// <summary>Tìm theo nội dung chỉ trong một post (phân trang).</summary>
    Task<(List<Comment> Items, long TotalCount)> SearchByContentInPostPagedAsync(
        Guid postId,
        string contentContains,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<Comment?> GetByIdAsync(Guid id);
    Task AddAsync(Comment comment);
    void Update(Comment comment);
    void Remove(Comment comment);
    Task<bool> ExistsAsync(Guid id);
    Task<bool> PostExistsAsync(Guid postId);
    Task<bool> UserExistsAsync(Guid userId);
    Task<bool> ParentExistsAsync(Guid parentId, Guid postId);
    Task<List<CommentFlatDto>> GetTreeRowsByCteAsync(Guid postId);

    /// <summary>CTE đệ quy trên toàn bộ comment (mọi post); join ParentId kèm PostId.</summary>
    Task<List<CommentFlatDto>> GetTreeRowsByCteAllAsync();

    /// <summary>Demo lazy loading: chỉ query Comment, sau đó chạm navigation → proxy nạp thêm SQL.</summary>
    Task<CommentLoadingDemoDto?> GetCommentLazyLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Demo eager loading: Include Post, User, Parent, Children (AsSplitQuery).</summary>
    Task<CommentLoadingDemoDto?> GetCommentEagerLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Demo explicit loading: Entry().Reference / Collection LoadAsync từng phần.</summary>
    Task<CommentLoadingDemoDto?> GetCommentExplicitLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Demo projection (một bản ghi): Select trực tiếp DTO, EF dịch join/subquery trên SQL.</summary>
    Task<CommentLoadingDemoDto?> GetCommentProjectionDemoAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Phân trang + lazy: mỗi dòng tracked, đọc navigation kích hoạt thêm truy vấn.</summary>
    Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsLazyLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Phân trang + eager: Include Post, User, Parent, Children (AsSplitQuery).</summary>
    Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsEagerLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Phân trang + explicit: sau Skip/Take, LoadAsync từng navigation cho từng comment.</summary>
    Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsExplicitLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Phân trang + projection: một truy vấn Select, không Include.</summary>
    Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsProjectionDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync();
}
