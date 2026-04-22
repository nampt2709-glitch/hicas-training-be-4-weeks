using CommentAPI.DTOs;

// Hợp đồng dịch vụ: quy tắc nghiệp vụ, phân quyền tác giả/admin, tổ chức cây/CTE và demo load.
namespace CommentAPI.Interfaces;

// Giao diện service Comment: CRUD, tìm kiếm, cây phẳng/CTE, demo EF loading; implement gọi repository + mapping.
public interface ICommentService
{
    // Danh sách phân trang tất cả comment (Dạng DTO phẳng) — theo số trang/ kích thước.
    Task<PagedResult<CommentDto>> GetAllPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Toàn bộ comment phẳng của một bài trong một lần đọc — không phân trang (khác GetFlatByPostIdPagedAsync bị giới hạn pageSize).
    Task<IReadOnlyList<CommentDto>> GetAllByPostIdAsync(
        Guid postId,
        CancellationToken cancellationToken = default);

    // Lấy đúng một comment theo id; ném lỗi nếu không tồn tại tùy implement.
    Task<CommentDto> GetByIdAsync(Guid id);
    // Tạo mới: kiểm tra post, parent, tác giả, chống lặp chu trình thông qua service implement.
    Task<CommentDto> CreateAsync(CreateCommentDto dto);

    // Tác giả: chỉ sửa nội dung comment do chính mình tạo (currentUserId phải khớp).
    Task UpdateAsAuthorAsync(Guid id, UpdateCommentDto dto, Guid currentUserId);

    // Admin: cập nhật mọi trường hợp lệ, gồm chuyển bài/ cây với kiểm tra vòng thừa kế.
    Task UpdateAsAdminAsync(Guid id, AdminUpdateCommentDto dto);

    // Xoá: có thể xoá cứng/ mềm tùy cấu hình entity; implement enforce quyền nếu cần.
    Task DeleteAsync(Guid id);

    // Tìm theo nội dung (chuỗi con), phân trang toàn hệ thống — content null/ rỗng tùy quy ước lớp dưới.
    Task<PagedResult<CommentDto>> SearchByContentPagedAsync(
        string? content,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Lấy một comment theo id khi nằm trong post cụ thể (khớp cặp post–comment).
    Task<CommentDto> GetByIdInPostAsync(Guid postId, Guid commentId, CancellationToken cancellationToken = default);

    // Tìm theo nội dung nhưng giới hạn một post, có phân trang.
    Task<PagedResult<CommentDto>> SearchByContentInPostPagedAsync(
        Guid postId,
        string? content,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Tất cả comment dạng DTO phẳng, phân trang (list đơn giản, không cấu trúc cây).
    Task<PagedResult<CommentDto>> GetAllFlatPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Tất cả comment tổ chức thành cây (con nested), phân trang ở mức gốc hoặc theo cách gom cây của implement.
    Task<PagedResult<CommentTreeDto>> GetAllTreePagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Tất cả comment: dùng dòng phẳng từ CTE, phân trang theo tập phẳng.
    Task<PagedResult<CommentFlatDto>> GetAllCteFlatPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Từ dữ liệu CTE xây cây rồi gói thành CommentTreeDto, phân trang tùy chiến lược cây.
    Task<PagedResult<CommentTreeDto>> GetAllCteTreePagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Rừng cây từ EF: làm phẳng theo thứ tự DFS/preorder, không dùng CTE SQL.
    Task<PagedResult<CommentFlatDto>> GetFlattenedForestPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Toàn cục: CTE gom mọi post, xây cây, làm phẳng, rồi phân trang trên tập dòng phẳng.
    Task<PagedResult<CommentFlatDto>> GetFlattenedFromCtePagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Một post: list phẳng (DTO thường) có phân trang.
    Task<PagedResult<CommentDto>> GetFlatByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Một post: dữ liệu phẳng từ CTE, phân trang.
    Task<PagedResult<CommentFlatDto>> GetCteFlatByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Một post: cây từ load EF/ memory (không CTE) có phân trang.
    Task<PagedResult<CommentTreeDto>> GetTreeByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Một post: cây dựa trên CTE, phân trang.
    Task<PagedResult<CommentTreeDto>> GetCteTreeByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Một post: cây làm phẳng theo cách tổ chức đệ quy ở tầng ứng dụng.
    Task<PagedResult<CommentFlatDto>> GetFlattenedTreeByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Cây từ CTE theo post rồi làm phẳng preorder; Level/ độ sâu theo DFS tùy implement.
    Task<PagedResult<CommentFlatDto>> GetFlattenedCteTreeByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Demo API: lazy load — đọc navigation gây thêm truy vấn.
    Task<CommentLoadingDemoDto> GetCommentLazyLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default);

    // Demo API: eager load — Include/ split query giảm N+1 khi cần graph đủ lớn.
    Task<CommentLoadingDemoDto> GetCommentEagerLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default);

    // Demo API: explicit — LoadAsync từng quan hệ theo từng bước.
    Task<CommentLoadingDemoDto> GetCommentExplicitLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default);

    // Demo API: projection — Select trực tiếp DTO, tối ưu SQL.
    Task<CommentLoadingDemoDto> GetCommentProjectionDemoAsync(Guid id, CancellationToken cancellationToken = default);

    // Demo phân trang + lazy: nhiều dòng, mỗi dòng có thể kích hoạt truy vấn khi mở nav.
    Task<PagedResult<CommentLoadingDemoDto>> GetCommentsLazyLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Demo phân trang + eager: Include tập quan hệ, thường tách câu truy vấn.
    Task<PagedResult<CommentLoadingDemoDto>> GetCommentsEagerLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Demo phân trang + explicit: tải trang rồi Load từng mục.
    Task<PagedResult<CommentLoadingDemoDto>> GetCommentsExplicitLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Demo phân trang + projection: một Select cho nhiều bản ghi, không mở graph đầy đủ.
    Task<PagedResult<CommentLoadingDemoDto>> GetCommentsProjectionDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
