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



    // Danh sách user phân trang; name / userName / email là filter Contains tuỳ chọn (query).

    [HttpGet] // GET danh sách có phân trang.

    [Authorize(Roles = "Admin,User")] // Cả Admin và User được xem danh sách (theo chính sách nghiệp vụ).

    public async Task<IActionResult> GetAll( // Trả về trang user và tổng số bản ghi.

        [FromQuery] string? page, // Chuỗi số trang từ query (parse ở PaginationQuery).

        [FromQuery] string? pageSize, // Chuỗi kích thước trang.

        [FromQuery] string? name = null, // Filter: Name chứa chuỗi.

        [FromQuery] string? userName = null, // Filter: UserName chứa chuỗi.

        [FromQuery] string? email = null, // Filter: Email chứa chuỗi.

        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.

        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.

        CancellationToken cancellationToken = default) // Hủy khi client đóng kết nối.

    {

        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu from > to.

        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Chuẩn hóa page/pageSize an toàn.

        var result = await _service.GetPagedAsync(p, s, cancellationToken, createdAtFrom, createdAtTo, name, userName, email); // DB/cache qua service.

        return Ok(new { message = ApiMessages.UserListSuccess, data = result }); // 200 kèm message và dữ liệu phân trang.

    }



    [HttpGet("{id:guid}")] // GET một user theo id — GET /api/users/{id}.

    [Authorize(Roles = "Admin,User")] // Đọc chi tiết.

    public async Task<IActionResult> GetById(Guid id) // Guid trong route template.

    {

        var result = await _service.GetByIdAsync(id); // Cache-aside + DB trong service.

        return Ok(new { message = ApiMessages.UserGetSuccess, data = result }); // 200.

    }



    [HttpPost] // POST tạo user mới (Admin).

    [Authorize(Roles = "Admin")] // Chỉ quản trị tạo tài khoản, còn nếu user muốn tạo tài khoản thì phải sign up

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


