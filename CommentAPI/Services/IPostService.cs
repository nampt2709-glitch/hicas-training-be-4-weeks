using CommentAPI.DTOs;

// Hợp đồng tầng dịch vụ: triển khai lớp PostService, dùng từ controller, DI, test.
namespace CommentAPI.Interfaces;

// Nghiệp vụ bài: danh sách, tìm, chi tiết, tạo, cập nhật (tác giả/ admin), xóa; tầng dịch vụ, không chèn câu SQL ở đây.
public interface IPostService
{
    // Trả một trang bài kèm tổng; page/pageSize đã chuẩn hóa ở tầng gọi hoặc ở đây tùy quy ước; cache-aside ở triển khai.
    Task<PagedResult<PostDto>> GetPagedAsync(
        int page, // Số trang, bắt đầu 1, kết hợp Skip: (page-1)*pageSize.
        int pageSize, // Số bản ghi tối đa mỗi trang, chặn trên theo cấu hình phân trang.
        CancellationToken cancellationToken = default, // Hủy tác vụ bất đồng bộ khi client hủy request, — truyền xuống EF.
        DateTime? createdAtFrom = null, // Lọc CreatedAt inclusive.
        DateTime? createdAtTo = null, // Lọc CreatedAt inclusive.
        string? titleContains = null, // Filter Contains trên Title (tuỳ chọn).
        string? contentContains = null); // Filter Contains trên Content (tuỳ chọn).

    // Một bài theo id; ném hoặc trả 404 tùy triển khai, — cache đọc theo khóa post id.
    Task<PostDto> GetByIdAsync(Guid id);
    // Tạo bài từ DTO, gán Id/CreatedAt trong service, — map tới entity, — ghi database.
    Task<PostDto> CreateAsync(CreatePostDto dto);
    // Chỉ tác giả (currentUserId) được sửa tiêu đề/ nội dung, — 403/404 theo từng bản thể.
    Task UpdateAsAuthorAsync(Guid id, UpdatePostDto dto, Guid currentUserId);
    // Cập nhật đủ, — có thể gán UserId, — tầng ủy quyền Admin ngoài interface.
    Task UpdateAsAdminAsync(Guid id, AdminUpdatePostDto dto);
    // Xóa bài, — cập nhật/ xóa cache liên quan, — ủy quyền Admin theo route.
    Task DeleteAsync(Guid id);
}
