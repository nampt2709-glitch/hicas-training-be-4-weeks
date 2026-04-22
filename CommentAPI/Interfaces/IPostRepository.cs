using CommentAPI.DTOs;
using CommentAPI.Entities;

// Hợp đồng truy cập bảng Posts, tách khỏi service.
namespace CommentAPI.Interfaces;

// Truy vấn bài: list, phân trang projection, tìm tiêu đề, đọc theo id (DTO hoặc entity), CRUD, lưu.
public interface IPostRepository
{
    Task<List<Post>> GetAllAsync(); // Toàn bộ entity (dùng hiếm, thường kèm AsNoTracking ở triển khai).

    // Phân trang, trả trực tiếp PostDto từ SQL (Select), không materialize Post rồi map thủ công.
    Task<(List<PostDto> Items, long TotalCount)> GetPagedAsync(
        int page, // Trang, dùng OFFSET.
        int pageSize, // FETCH, giới hạn hàng.
        CancellationToken cancellationToken = default); // Hủy.

    // Tìm Title chứa chuỗi, phân trang; count + trang song song.
    Task<(List<PostDto> Items, long TotalCount)> SearchByTitlePagedAsync(
        string titleContains, // Chuỗi con, đã trim/validate ở tầng trên nếu cần.
        int page, // Trang.
        int pageSize, // Cỡ.
        CancellationToken cancellationToken = default); // Hủy.

    // Đọc một dòng PostDto theo id, AsNoTracking, cho cache/ response; null nếu không có.
    Task<PostDto?> GetByIdForReadAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Post?> GetByIdAsync(Guid id); // Entity để sửa/xóa, có thể tracked.
    Task AddAsync(Post post); // Thêm bài mới.
    void Update(Post post); // Cập nhật.
    void Remove(Post post); // Xóa.
    Task<bool> ExistsAsync(Guid id); // Any theo id.     
    Task SaveChangesAsync(); // Commit thay đổi.
}
