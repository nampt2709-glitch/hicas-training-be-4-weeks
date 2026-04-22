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

    // Danh sách post có phân trang; mặc định page=1, pageSize=20.
    [HttpGet] // GET paged list.
    [Authorize(Roles = "Admin,User")] // Đọc bài.
    public async Task<IActionResult> GetAll( // Phân trang qua query.
        [FromQuery] string? page, // Trang dạng chuỗi.
        [FromQuery] string? pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Chuẩn hóa.
        var result = await _service.GetPagedAsync(p, s, cancellationToken); // DB projection PostDto + cache.
        return Ok(new { message = ApiMessages.PostListSuccess, data = result }); // 200.
    }

    // Tìm đúng một post theo Id.
    [HttpGet("search/id/{id:guid}")] // Endpoint tìm theo id (alias semantic search).
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> SearchById(Guid id) // Guid post.
    {
        var result = await _service.GetByIdAsync(id); // Giống GetById.
        return Ok(new { message = ApiMessages.PostGetSuccess, data = result }); // 200.
    }

    // Tìm post theo tiêu đề (chuỗi chứa).
    [HttpGet("search/by-title")] // GET search title.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> SearchByTitle( // Phân trang.
        [FromQuery] string? title, // Mẫu tiêu đề.
        [FromQuery] string? page, // Trang.
        [FromQuery] string? pageSize, // Page size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.SearchByTitlePagedAsync(title, p, s, cancellationToken); // Service bắt buộc term.
        return Ok(new { message = ApiMessages.PostSearchByTitleSuccess, data = result }); // 200.
    }

    [HttpGet("{id:guid}")] // GET by id REST.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetById(Guid id) // Id.
    {
        var result = await _service.GetByIdAsync(id); // Cache + repo.
        return Ok(new { message = ApiMessages.PostGetSuccess, data = result }); // 200.
    }

    [HttpPost] // Tạo bài mới.
    [Authorize(Roles = "Admin")] // Chỉ Admin tạo thay user khác (UserId trong body).
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
