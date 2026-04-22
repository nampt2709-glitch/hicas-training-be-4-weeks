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
        CancellationToken cancellationToken = default); // Truyền xuống EF/SQL để hủy khi request đóng.

    // Tìm user có Name chứa chuỗi, phân trang; name null/rỗng xử lý theo quy ước validator hoặc service.
    Task<PagedResult<UserDto>> SearchByNamePagedAsync(
        string? name, // Chuỗi con tìm trong cột Name; có thể null nếu route cho phép.
        int page, // Trang phân trang, dùng OFFSET tính từ (page-1)*pageSize.
        int pageSize, // Cỡ trang, giới hạn tải dữ liệu mỗi lần.
        CancellationToken cancellationToken = default); // Hủy tác vụ khi client ngắt kết nối.

    // Tìm user có UserName chứa chuỗi, phân trang; dùng cho ô tìm kiếm theo đăng nhập.
    Task<PagedResult<UserDto>> SearchByUserNamePagedAsync(
        string? userName, // Chuỗi con tìm trong UserName; null/ rỗng theo quy ước tầng trên.
        int page, // Trang 1-based sau chuẩn hóa.
        int pageSize, // Kích thước trang, bị cắt theo MaxPageSize.
        CancellationToken cancellationToken = default); // Token hủy dùng trong truy vấn dài.

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
