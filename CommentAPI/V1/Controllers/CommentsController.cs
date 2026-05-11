using CommentAPI; // ApiException, PaginationQuery, CreatedAtRangeQuery dùng chung.
using Asp.Versioning; // [ApiVersion] gắn phiên bản 1.0 vào controller.
using CommentAPI.Controllers; // HttpContextUserId lấy user id bắt buộc khi cần.
using CommentAPI.DTOs; // CreateCommentDto, UpdateCommentDto, phân trang.
using CommentAPI.Interfaces; // ICommentService, ICommentRepository.
using CommentAPI.Validators; // FluentValidation cho body comment.
using Microsoft.AspNetCore.Authorization; // [Authorize(Roles = "...")].
using Microsoft.AspNetCore.Http; // StatusCodes trong một số phản hồi lỗi có chủ đích.
using Microsoft.AspNetCore.Mvc; // ControllerBase, IActionResult, FromQuery/FromBody.

namespace CommentAPI.V1.Controllers;

// =============================================================================
// V1 — chỉ CRUD comment cơ bản (danh sách phân trang, theo id, tạo, sửa tác giả, xóa).
// Đọc theo user, admin reparent, flat/CTE/cây, demo loading… → API v2.0.
// =============================================================================

[ApiController]
[ApiVersion("1.0")]
[Authorize]
[Route("api/v{version:apiVersion}/comments")]
public class CommentsController : ControllerBase
{
    private readonly ICommentService _service;
    private readonly ICommentRepository _commentRepository;

    public CommentsController(ICommentService service, ICommentRepository commentRepository)
    {
        _service = service;
        _commentRepository = commentRepository;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? postId,
        [FromQuery] Guid? userId,
        [FromQuery] string? content,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken cancellationToken = default)
    {
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var sortSpec = _commentRepository.ParseCommentListSortOrThrow(sort, sortDir);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetCommentListAsync(postId, content, false, p, s, cancellationToken, createdAtFrom, createdAtTo, userId, sortSpec);
        return Ok(new { message = ApiMessages.CommentListSuccess, data = result });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _service.GetByIdAsync(id);
        return Ok(new { message = ApiMessages.CommentGetSuccess, data = result });
    }

    [HttpPost]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> Create([FromBody] CreateCommentDto dto)
    {
        var result = await _service.CreateAsync(dto);
        return Ok(new { message = ApiMessages.CommentCreateSuccess, data = result });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCommentDto dto)
    {
        if (User.IsInRole("Admin"))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    code = ApiErrorCodes.UseAdminUpdateEndpoint,
                    message = ApiMessages.UseAdminUpdateEndpoint
                });
        }

        var userId = HttpContextUserId.GetRequiredUserId(User);
        await _service.UpdateAsAuthorAsync(id, dto, userId);
        return Ok(new { message = ApiMessages.CommentUpdateSuccess });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.DeleteAsync(id);
        return Ok(new { message = ApiMessages.CommentDeleteSuccess });
    }
}
