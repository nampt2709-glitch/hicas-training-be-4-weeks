using CommentAPI;
using CommentAPI.DTOs; 
using CommentAPI.Interfaces;
using Microsoft.AspNetCore.Authorization; 
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc; 

namespace CommentAPI.Controllers;

[ApiController] // Controller kiểu API (binding, lỗi validation chuẩn).
[Authorize] // Mặc định mọi action cần đã xác thực (trừ khi ghi đè).
[Route("api/users")] // Tiền tố URI cho tài nguyên User.
public class UsersController : ControllerBase // Không trả view Razor.
{
    private readonly IUserService _service; // Dịch vụ tầng ứng dụng cho user.

    public UsersController(IUserService service) // DI container inject IUserService.
    {
        _service = service; // Lưu tham chiếu phục vụ các action.
    }

    // Danh sách user có phân trang; mặc định page=1, pageSize=20 nếu không gửi query.
    [HttpGet] // GET danh sách có phân trang.
    [Authorize(Roles = "Admin,User")] // Cả Admin và User được xem danh sách (theo chính sách nghiệp vụ).
    public async Task<IActionResult> GetAll( // Trả về trang user và tổng số bản ghi.
        [FromQuery] string? page, // Chuỗi số trang từ query (parse ở PaginationQuery).
        [FromQuery] string? pageSize, // Chuỗi kích thước trang.
        CancellationToken cancellationToken = default) // Hủy khi client đóng kết nối.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Chuẩn hóa page/pageSize an toàn.
        var result = await _service.GetPagedAsync(p, s, cancellationToken); // Lấy trang từ DB/cache qua service.
        return Ok(new { message = ApiMessages.UserListSuccess, data = result }); // 200 kèm message và dữ liệu phân trang.
    }

    // Tìm đúng một user theo Id — trả đủ trường như GET by id.
    [HttpGet("search/id/{id:guid}")] // Route tìm nhanh theo guid trong path.
    [Authorize(Roles = "Admin,User")] // Quyền đọc giống GetById.
    public async Task<IActionResult> SearchById(Guid id) // Khóa chính user.
    {
        var result = await _service.GetByIdAsync(id); // Cùng logic với GetById: một bản ghi hoặc 404 từ service.
        return Ok(new { message = ApiMessages.UserGetSuccess, data = result }); // 200 với DTO user.
    }

    // Tìm user theo UserDto.Name (chuỗi chứa, không phân biệt hoa thường tùy repository).
    [HttpGet("search/by-name")] // GET tìm theo tên hiển thị.
    [Authorize(Roles = "Admin,User")] // Ai có quyền list thì tìm theo tên.
    public async Task<IActionResult> SearchByName( // Phân trang kết quả tìm kiếm.
        [FromQuery] string? name, // Mẫu chứa trong cột Name.
        [FromQuery] string? page, // Trang.
        [FromQuery] string? pageSize, // Kích thước trang.
        CancellationToken cancellationToken = default) // Token hủy.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Giới hạn và mặc định phân trang.
        var result = await _service.SearchByNamePagedAsync(name, p, s, cancellationToken); // Truy vấn LIKE + phân trang.
        return Ok(new { message = ApiMessages.UserSearchByNameSuccess, data = result }); // 200 với PagedResult.
    }

    // Tìm user theo UserDto.UserName (chuỗi chứa).
    [HttpGet("search/by-username")] // GET tìm theo tên đăng nhập.
    [Authorize(Roles = "Admin,User")] // Quyền đọc user.
    public async Task<IActionResult> SearchByUserName( // Phân trang.
        [FromQuery] string? userName, // Mẫu chứa trong UserName.
        [FromQuery] string? page, // Trang.
        [FromQuery] string? pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy bất đồng bộ.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse an toàn.
        var result = await _service.SearchByUserNamePagedAsync(userName, p, s, cancellationToken); // Tìm theo username.
        return Ok(new { message = ApiMessages.UserSearchByUserNameSuccess, data = result }); // 200.
    }

    [HttpGet("{id:guid}")] // GET một user theo id chuẩn REST.
    [Authorize(Roles = "Admin,User")] // Đọc chi tiết.
    public async Task<IActionResult> GetById(Guid id) // Guid trong route template.
    {
        var result = await _service.GetByIdAsync(id); // Cache-aside + DB trong service.
        return Ok(new { message = ApiMessages.UserGetSuccess, data = result }); // 200.
    }

    [HttpPost] // POST tạo user mới (Admin).
    [Authorize(Roles = "Admin")] // Chỉ quản trị tạo tài khoản.
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto) // Body validation FluentValidation.
    {
        var result = await _service.CreateAsync(dto); // Identity UserManager + role mặc định.
        return CreatedAtAction( // 201 Location trỏ tới GetById.
            nameof(GetById), // Tên action để build URL.
            new { id = result.Id }, // Route values cho id mới.
            new { message = ApiMessages.UserCreateSuccess, data = result }); // Payload phản hồi.
    }

    // User (không phải Admin): chỉ sửa Name của chính mình; Admin phải dùng PUT /api/admin/users/{id}.
    [HttpPut("{id:guid}")] // PUT cập nhật Name cho tài khoản hiện tại.
    [Authorize(Roles = "Admin,User")] // User đổi tên hiển thị; Admin bị chặn và được hướng dẫn endpoint admin.
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDto dto) // Id phải trùng user trong JWT.
    {
        if (User.IsInRole("Admin")) // Admin không được dùng endpoint hồ sơ giới hạn.
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    code = ApiErrorCodes.UserUseAdminUpdateEndpoint,
                    message = ApiMessages.UserUseAdminUpdateEndpoint
                });
        }

        var userId = HttpContextUserId.GetRequiredUserId(User); // Guid từ JWT.
        await _service.UpdateAsSelfAsync(id, dto, userId); // Kiểm tra id == userId trong service.
        return Ok(new { message = ApiMessages.UserUpdateSuccess }); // 200.
    }

    // Admin: cập nhật đầy đủ UserName, Email, Name, roles, mật khẩu tùy chọn (route tuyệt đối giống pattern comment admin).
    [HttpPut("~/api/admin/users/{id:guid}")] // PUT .../api/admin/users/{id}
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateAsAdmin(Guid id, [FromBody] AdminUpdateUserDto dto)
    {
        await _service.UpdateAsAdminAsync(id, dto); // Trùng username/email và admin cuối xử lý trong service.
        return Ok(new { message = ApiMessages.UserAdminUpdateSuccess }); // 200.
    }

    [HttpDelete("{id:guid}")] // DELETE xóa user khỏi Identity/DB.
    [Authorize(Roles = "Admin")] // Chỉ Admin.
    public async Task<IActionResult> Delete(Guid id) // Xóa cứng theo id.
    {
        await _service.DeleteAsync(id); // UserManager.Delete + xóa cache.
        return Ok(new { message = ApiMessages.UserDeleteSuccess }); // 200 xác nhận.
    }
}
