using Asp.Versioning; // Thuộc tính [ApiVersion] gắn phiên bản lên controller/action.
using CommentAPI; // ApiException và lỗi nghiệp vụ dùng chung.
using CommentAPI.Controllers; // HttpContextUserId: lấy user id bắt buộc từ ClaimsPrincipal.
using CommentAPI.Versioning; // ApiVersionRouteValues.WithVersion cho CreatedAtRoute kèm segment version.

using CommentAPI.DTOs; // CreatePostDto, UpdatePostDto, AdminUpdatePostDto, phản hồi phân trang.
using CommentAPI.Validators; // FluentValidation: validate body trước khi gọi service.

using CommentAPI.Interfaces; // IPostService, IPostRepository, ICommentService.

using Microsoft.AspNetCore.Authorization; // [Authorize], phân quyền theo JWT/Roles.

using Microsoft.AspNetCore.Http; // StatusCodes (403, v.v.) trong phản hồi có chủ đích.

using Microsoft.AspNetCore.Mvc; // ControllerBase, IActionResult, routing attributes.



namespace CommentAPI.V2.Controllers; 



[ApiController] // API controller.
[ApiVersion("2.0")] // Phiên bản 2.0 trong URL (api/v2/...).

[Authorize] // JWT bắt buộc (trừ route công khai khác pipeline).

[Route("api/v{version:apiVersion}/posts")] // REST collection posts có segment version.

public class PostsController : ControllerBase // Không view.

{

    private readonly IPostService _service; // Nghiệp vụ post + cache.

    private readonly IPostRepository _postRepository; // Parse sort danh sách post.

    // CommentService: [2a]/[2b] — ICommentRepository.GetAllCommentsForPost thực thi CTE SQL riêng (không LoadRawCteAsync); query includeReplies (mặc định true nếu bỏ qua).
    private readonly ICommentService _commentService;

    private readonly ICommentRepository _commentRepository; // Parse sort comment sub-route.

    public PostsController(
        IPostService service,
        IPostRepository postRepository,
        ICommentService commentService,
        ICommentRepository commentRepository) // DI: post + post repo + comment cho sub-resource.

    {

        _service = service; // Gán field post.

        _postRepository = postRepository; // Parse sort list post.

        _commentService = commentService; // Tree/flat theo postId route; không tham số filter trên URL.

        _commentRepository = commentRepository; // Parse sort query comment.

    }



    // [1] GET /api/posts
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

        [FromQuery] string? sort = null, // Cột PostDto.

        [FromQuery] string? sortDir = null, // asc hoặc desc.

        CancellationToken cancellationToken = default) // Hủy.

    {

        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu from > to.

        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Chuẩn hóa.

        var sortSpec = _postRepository.ParsePostListSortOrThrow(sort, sortDir);

        var result = await _service.GetPagedAsync(p, s, cancellationToken, createdAtFrom, createdAtTo, title, content, sortSpec); // DB + cache khi không filter.

        return Ok(new { message = ApiMessages.PostListSuccess, data = result }); // 200.

    }



    // [2] GET /api/posts/{id}
    [HttpGet("{id:guid}")] // GET một post theo id — GET /api/posts/{id}.

    [Authorize(Roles = "Admin,User")] // Đọc.

    public async Task<IActionResult> GetById(Guid id) // Id.

    {
        var result = await _service.GetByIdAsync(id); // Cache + repo.

        return Ok(new { message = ApiMessages.PostGetSuccess, data = result }); // 200.

    }



    // [2a] GET /api/posts/{id}/comments/tree
    // Sub-resource: postId trên route; includeReplies query (null = true). Repo: CTE riêng GetAllCommentsForPost + service BuildTreeCte.
    [HttpGet("{id:guid}/comments/tree")] // Sub-resource: cây comment theo một post.

    [Authorize(Roles = "Admin,User")] // Đọc comment.

    public async Task<IActionResult> GetCommentsTreeByPostId(Guid id, [FromQuery] bool? includeReplies, [FromQuery] string? sort, [FromQuery] string? sortDir, CancellationToken cancellationToken) // id = PostId; includeReplies + sort (dropdown) tuỳ chọn.

    { // Mở khối GetCommentsTreeByPostId.
        // BƯỚC 1 — Parse sort an toàn rồi gọi service: EnsurePost + GetAllCommentsForPost (CTE repo) + BuildTreeCte.
        var sortSpec = _commentRepository.ParseCommentListSortOrThrow(sort, sortDir); // 400 nếu sort không hợp lệ.
        var data = await _commentService.GetCommentsTreeForPostAsync(id, includeReplies ?? true, sortSpec, cancellationToken); // Cache riêng l:posts:…:comments:tree:cte:…; null includeReplies → gốc + reply.

        // BƯỚC 2 — 200 + message mô tả đúng nguồn CTE (khác message route tree/flat RAM trên /api/comments).
        return Ok(new { message = ApiMessages.CommentCteTreeByPostSuccess, data }); // data = cây từ CTE.

    } // Kết thúc GetCommentsTreeByPostId.



    // [2b] GET /api/posts/{id}/comments/flat
    // Sub-resource: postId; includeReplies (null = true). Repo CTE GetAllCommentsForPost — phẳng có Level, không nested.
    [HttpGet("{id:guid}/comments/flat")] // Sub-resource: list phẳng theo một post.

    [Authorize(Roles = "Admin,User")] // Đọc comment.

    public async Task<IActionResult> GetCommentsFlatByPostId(Guid id, [FromQuery] bool? includeReplies, [FromQuery] string? sort, [FromQuery] string? sortDir, CancellationToken cancellationToken) // id = PostId.

    { // Mở khối GetCommentsFlatByPostId.
        // BƯỚC 1 — Parse sort rồi gọi CTE phẳng theo bài (không BuildTreeCte).
        var sortSpec = _commentRepository.ParseCommentListSortOrThrow(sort, sortDir); // 400 nếu sort sai.
        var data = await _commentService.GetCommentsFlatForPostAsync(id, includeReplies ?? true, sortSpec, cancellationToken); // Không phân trang; cache khóa l:posts:…:comments:flat:cte:….

        // BƯỚC 2 — 200 + message CTE phẳng theo bài (có Level trên mỗi dòng).
        return Ok(new { message = ApiMessages.CommentCteFlatByPostSuccess, data }); // data = List<CommentCteDto> từ SqlQueryRaw (CTE).

    } // Kết thúc GetCommentsFlatByPostId.



    // [3] POST /api/posts
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



    // [4] PUT /api/posts/{id}
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



    // [5] PUT /api/admin/posts/{id}
    // Route tuyệt đối: Admin dùng DTO mở rộng (có thể gán lại UserId) — tách hẳn với cập nhật của tác giả ở api/posts.

    [HttpPut("~/api/v{version:apiVersion}/admin/posts/{id:guid}")] // Admin: tuyệt đối, có segment version.

    [Authorize(Roles = "Admin")] // Chỉ Admin.

    public async Task<IActionResult> UpdateAsAdmin(Guid id, [FromBody] AdminUpdatePostDto dto) // UserId optional.

    {

        await _service.UpdateAsAdminAsync(id, dto); // Cập nhật + đổi chủ nếu có UserId.

        return Ok(new { message = ApiMessages.PostUpdateSuccess }); // 200.

    }



    // [6] DELETE /api/posts/{id}
    [HttpDelete("{id:guid}")] // Xóa bài.

    [Authorize(Roles = "Admin")] // Chỉ Admin xóa.

    public async Task<IActionResult> Delete(Guid id) // Id.

    {

        await _service.DeleteAsync(id); // Remove entity + invalidate cache key post.

        return Ok(new { message = ApiMessages.PostDeleteSuccess }); // 200.

    }

}


