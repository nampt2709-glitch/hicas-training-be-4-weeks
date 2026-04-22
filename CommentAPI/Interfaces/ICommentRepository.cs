using CommentAPI.DTOs;
using CommentAPI.Entities;

// Không gian tên tập hợp hợp đồng (interface) tầng dữ liệu.
namespace CommentAPI.Interfaces;

// Hợp đồng truy cập dữ liệu Comment: CRUD, phân trang, CTE, demo loading, phục vụ service.
public interface ICommentRepository
{
    // Lấy mọi comment (không phân trang) — cẩn thận khối lượng dữ liệu lớn.
    Task<List<Comment>> GetAllAsync();

    // Phân trang comment: trả (danh sách, tổng số) — hỗ trợ bảng/ offset-paging.
    Task<(List<Comment> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Phân trang comment theo postId — dùng khi cần danh sách theo từng bài.
    Task<(List<Comment> Items, long TotalCount)> GetByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Phân trang các comment gốc (ParentId = null) trên toàn hệ thống.
    Task<(List<Comment> Items, long TotalCount)> GetRootCommentsPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Phân trang comment gốc theo từng post.
    Task<(List<Comment> Items, long TotalCount)> GetRootsByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Tìm comment có Content chứa chuỗi, có phân trang — bộ lọc toàn hệ thống.
    Task<(List<Comment> Items, long TotalCount)> SearchByContentPagedAsync(
        string contentContains,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Tải comment cho nhiều post cùng lúc (tránh N+1) — dùng IN postIds.
    Task<List<Comment>> GetCommentsForPostsAsync(
        IReadOnlyCollection<Guid> postIds,
        CancellationToken cancellationToken = default);

    // Lấy tất cả comment của một bài; thường AsNoTracking khi phục vụ API đọc.
    Task<List<Comment>> GetByPostIdAsync(Guid postId);

    // Giống GetByPostIdAsync nhưng entity được theo dõi (track) — phục vụ cập nhật hàng loạt, ví dụ chuyển cây sang post khác.
    Task<List<Comment>> GetByPostIdTrackedAsync(
        Guid postId,
        CancellationToken cancellationToken = default);

    // Đọc một comment theo id — chiếu ra CommentDto, không track (AsNoTracking).
    Task<CommentDto?> GetByIdForReadAsync(Guid id, CancellationToken cancellationToken = default);

    // Đọc comment theo id khi thuộc về post cụ thể (kiểm tra tính hợp lệ cặp post–comment).
    Task<CommentDto?> GetByIdForReadInPostAsync(Guid postId, Guid commentId, CancellationToken cancellationToken = default);

    // Tìm theo nội dung trong phạm vi một post, có phân trang.
    Task<(List<Comment> Items, long TotalCount)> SearchByContentInPostPagedAsync(
        Guid postId,
        string contentContains,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Lấy entity theo id — có thể trả null nếu không tồn tại; thường dùng khi cần cập nhật/xoá.
    Task<Comment?> GetByIdAsync(Guid id);
    // Thêm comment mới (chưa ghi DB nếu chưa gọi SaveChanges).
    Task AddAsync(Comment comment);
    // Đánh dấu thay đổi trên entity đã theo dõi.
    void Update(Comment comment);
    // Đánh dấu xoá (hoặc xoá mềm tùy cấu hình) trên context.
    void Remove(Comment comment);
    // Kiểm tra tồn tại theo id (có thể tối ưu EXISTS / Any).
    Task<bool> ExistsAsync(Guid id);
    // Kiểm tra post tồn tại (khóa ngoại khi tạo comment).
    Task<bool> PostExistsAsync(Guid postId);
    // Kiểm tra user tồn tại (gán Author).
    Task<bool> UserExistsAsync(Guid userId);
    // Kiểm tra bình luận cha thuộc cùng post (tránh liên kết chéo post).
    Task<bool> ParentExistsAsync(Guid parentId, Guid postId);
    // CTE đệ quy: trả tập phẳng cây comment của một post (dòng phẳng + depth/path tùy DTO).
    Task<List<CommentFlatDto>> GetTreeRowsByCteAsync(Guid postId);

    // CTE đệ quy trên toàn bộ comment (mọi post); join kèm PostId/ParentId phục vụ tổng hợp.
    Task<List<CommentFlatDto>> GetTreeRowsByCteAllAsync();

    // Demo lazy: chỉ query Comment, chạm navigation sẽ kích hoạt proxy tải thêm (nhiều round-trip).
    Task<CommentLoadingDemoDto?> GetCommentLazyLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default);

    // Demo eager: Include Post, User, Parent, Children, thường kèm AsSplitQuery.
    Task<CommentLoadingDemoDto?> GetCommentEagerLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default);

    // Demo explicit: LoadAsync từng quan hệ theo bước, kiểm soát từng truy vấn.
    Task<CommentLoadingDemoDto?> GetCommentExplicitLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default);

    // Demo projection: Select trực tiếp DTO — một câu SQL, không cần Include toàn bộ.
    Task<CommentLoadingDemoDto?> GetCommentProjectionDemoAsync(Guid id, CancellationToken cancellationToken = default);

    // Phân trang + lazy: mỗi dòng tracked, đọc navigation sinh thêm truy vấn (minh hoạ cẩn thận với số dòng lớn).
    Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsLazyLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Phân trang + eager: Include toàn bộ liên kết, thường tách truy vấn (split) để tránh Cartesian product.
    Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsEagerLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Phân trang + explicit: lấy trang rồi LoadAsync từng thực thể (nhiều câu lệnh nhỏ có kiểm soát).
    Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsExplicitLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Phân trang + projection: một truy vấn Select, không mở rộng graph entity.
    Task<(List<CommentLoadingDemoDto> Items, long TotalCount)> GetCommentsProjectionDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Lưu mọi thay đổi đang treo (insert/update/delete) xuống cơ sở dữ liệu.
    Task SaveChangesAsync();
}
