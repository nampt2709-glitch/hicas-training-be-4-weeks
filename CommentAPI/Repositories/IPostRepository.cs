using CommentAPI.DTOs;
using CommentAPI.Entities;

// Hợp đồng truy cập bảng Posts, tách khỏi service.
namespace CommentAPI.Interfaces;

// Truy vấn bài: list, phân trang projection, tìm tiêu đề, đọc theo id (DTO hoặc entity), CRUD, lưu.
public interface IPostRepository
{
    Task<List<Post>> GetAllAsync(); // Toàn bộ entity (dùng hiếm, thường kèm AsNoTracking ở triển khai).

    // Phân trang, trả trực tiếp PostDto từ SQL (Select); title/content là filter Contains tuỳ chọn (null = không lọc).
    Task<(List<PostDto> Items, long TotalCount)> GetPagedAsync(
        int page, // Trang, dùng OFFSET.
        int pageSize, // FETCH, giới hạn hàng.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt inclusive.
        DateTime? createdAtTo = null, // Lọc CreatedAt inclusive.
        string? titleContains = null, // Chuỗi con trong Title.
        string? contentContains = null); // Chuỗi con trong Content.

    // Đọc một dòng PostDto theo id, AsNoTracking, cho cache/ response; null nếu không có.
    Task<PostDto?> GetByIdForReadAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Post?> GetByIdAsync(Guid id); // Entity để sửa/xóa, có thể tracked.
    Task AddAsync(Post post); // Thêm bài mới.
    void Update(Post post); // Cập nhật.
    void Remove(Post post); // Xóa.
    Task<bool> ExistsAsync(Guid id); // Any theo id.     
    Task SaveChangesAsync(); // Commit thay đổi.
}
