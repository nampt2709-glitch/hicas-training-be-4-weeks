using CommentAPI;

using CommentAPI.DTOs;

using CommentAPI.Interfaces; 

using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Http; 

using Microsoft.AspNetCore.Mvc; 



namespace CommentAPI.Controllers; 



[ApiController] // API controller.

[Authorize] // JWT bắt buộc (trừ route công khai khác pipeline).

[Route("api/posts")] // REST collection posts.

public class PostsController : ControllerBase // Không view.

{

    private readonly IPostService _service; // Nghiệp vụ post + cache.



    public PostsController(IPostService service) // DI.

    {

        _service = service; // Gán field.

    }



    // Danh sách post phân trang; title/content là filter Contains tuỳ chọn (query).

    [HttpGet] // GET paged list.

    [Authorize(Roles = "Admin,User")] // Đọc bài.

    public async Task<IActionResult> GetAll( // Phân trang + filter.

        [FromQuery] string? page, // Trang dạng chuỗi.

        [FromQuery] string? pageSize, // Cỡ trang.

        [FromQuery] string? title = null, // Filter: tiêu đề chứa chuỗi.

        [FromQuery] string? content = null, // Filter: nội dung chứa chuỗi.

        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.

        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.

        CancellationToken cancellationToken = default) // Hủy.

    {

        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu from > to.

        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Chuẩn hóa.

        var result = await _service.GetPagedAsync(p, s, cancellationToken, createdAtFrom, createdAtTo, title, content); // DB + cache khi không filter.

        return Ok(new { message = ApiMessages.PostListSuccess, data = result }); // 200.

    }



    [HttpGet("{id:guid}")] // GET một post theo id — GET /api/posts/{id}.

    [Authorize(Roles = "Admin,User")] // Đọc.

    public async Task<IActionResult> GetById(Guid id) // Id.

    {
        var result = await _service.GetByIdAsync(id); // Cache + repo.

        return Ok(new { message = ApiMessages.PostGetSuccess, data = result }); // 200.

    }



    [HttpPost] // Tạo bài mới.

    [Authorize(Roles = "Admin,User")] // (UserId trong body).

    public async Task<IActionResult> Create([FromBody] CreatePostDto dto) // Title, Content, UserId.

    {

        var result = await _service.CreateAsync(dto); // Kiểm tra user tồn tại, insert post.

        return CreatedAtAction( // 201.

            nameof(GetById), // Action chi tiết.

            new { id = result.Id }, // Route value.

            new { message = ApiMessages.PostCreateSuccess, data = result }); // Body.

    }



    // User (không phải role Admin) cập nhật bài do chính mình; Admin bị yêu cầu dùng PUT /api/admin/posts/{id}.

    [HttpPut("{id:guid}")] // Cập nhật bài — luồng tác giả.

    [Authorize(Roles = "Admin,User")] // Cho phép User; Admin bị chặn ở dưới để tránh nhầm endpoint.

    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePostDto dto) // Chỉ title/content.

    {

        if (User.IsInRole("Admin")) // Admin phải dùng route admin riêng (đổi UserId được).

        {

            return StatusCode( // Trả 403 có payload JSON thống nhất (không throw).

                StatusCodes.Status403Forbidden, // 403.

                new // Object anonymous.

                {

                    code = ApiErrorCodes.UseAdminUpdateEndpoint, // Mã hướng dẫn client.

                    message = ApiMessages.UseAdminUpdateEndpoint // Thông điệp.

                });

        }



        var userId = HttpContextUserId.GetRequiredUserId(User); // Guid từ JWT; 401 nếu thiếu.

        await _service.UpdateAsAuthorAsync(id, dto, userId); // Kiểm tra entity.UserId == current.

        return Ok(new { message = ApiMessages.PostUpdateSuccess }); // 200.

    }



    // Route tuyệt đối: Admin dùng DTO mở rộng (có thể gán lại UserId) — tách hẳn với cập nhật của tác giả ở api/posts.

    [HttpPut("~/api/admin/posts/{id:guid}")] // Override route template: không nằm dưới api/posts prefix.

    [Authorize(Roles = "Admin")] // Chỉ Admin.

    public async Task<IActionResult> UpdateAsAdmin(Guid id, [FromBody] AdminUpdatePostDto dto) // UserId optional.

    {

        await _service.UpdateAsAdminAsync(id, dto); // Cập nhật + đổi chủ nếu có UserId.

        return Ok(new { message = ApiMessages.PostUpdateSuccess }); // 200.

    }



    [HttpDelete("{id:guid}")] // Xóa bài.

    [Authorize(Roles = "Admin")] // Chỉ Admin xóa.

    public async Task<IActionResult> Delete(Guid id) // Id.

    {

        await _service.DeleteAsync(id); // Remove entity + invalidate cache key post.

        return Ok(new { message = ApiMessages.PostDeleteSuccess }); // 200.

    }

}


