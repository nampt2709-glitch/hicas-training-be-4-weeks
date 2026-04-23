using CommentAPI.DTOs;

// Hợp đồng dịch vụ người dùng: tách khỏi controller và lớp UserRepository.
namespace CommentAPI.Interfaces;

// Bề mặt nghiệp vụ user: phân trang, tìm theo tên/ tên đăng nhập, CRUD; lỗi và cache do lớp triển khai.
public interface IUserService
{
    // Trả một trang danh sách user dạng DTO; page/pageSize đi kèm token hủy cho truy vấn bất đồng bộ.
    Task<PagedResult<UserDto>> GetPagedAsync(
        int page, // Số trang bắt đầu từ 1 sau khi chuẩn hóa ở client hoặc service.
        int pageSize, // Số bản ghi tối đa mỗi trang, có trần MaxPageSize ở PaginationQuery.
        CancellationToken cancellationToken = default, // Truyền xuống EF/SQL để hủy khi request đóng.
        DateTime? createdAtFrom = null, // Lọc CreatedAt inclusive.
        DateTime? createdAtTo = null, // Lọc CreatedAt inclusive.
        string? nameContains = null, // Filter Contains trên Name (tuỳ chọn).
        string? userNameContains = null, // Filter Contains trên UserName (tuỳ chọn).
        string? emailContains = null); // Filter Contains trên Email (tuỳ chọn).

    // Một user theo id; không có thì ném ngoại lệ 404 thống nhất API (tùy triển khai).
    Task<UserDto> GetByIdAsync(Guid id);
    // Tạo user: băm mật khẩu, gán role, trả DTO không có mật khẩu.
    Task<UserDto> CreateAsync(CreateUserDto dto);
    // User (không phải Admin): chỉ sửa Name của chính mình; id phải trùng currentUserId (kiểm tra ở service).
    Task UpdateAsSelfAsync(Guid id, UpdateUserDto dto, Guid currentUserId);
    // Admin: Name, UserName, Email, roles, mật khẩu tùy chọn — kiểm tra trùng username/email, role hợp lệ, không gỡ Admin khỏi admin cuối.
    Task UpdateAsAdminAsync(Guid id, AdminUpdateUserDto dto);
    // Xóa user; xóa cache user tương ứng trong triển khai có cache.
    Task DeleteAsync(Guid id);
}
