using CommentAPI; 
using CommentAPI.DTOs; 
using CommentAPI.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http; 
using Microsoft.AspNetCore.Mvc; 

namespace CommentAPI.Controllers; 

[ApiController] // Web API.
[Authorize] // JWT.
[Route("api/comments")] // Base path.
public class CommentsController : ControllerBase // JSON only.
{
    private readonly ICommentService _service; // Service comment phức tạp (cây, CTE, demo loading).

    public CommentsController(ICommentService service) // DI.
    {
        _service = service; // Field.
    }

    [HttpGet] // Danh sách comment phân trang (mặc định kiểu list service).
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAll( // Query phân trang.
        [FromQuery] string? page, // Page string.
        [FromQuery] string? pageSize, // Size string.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Normalize.
        var result = await _service.GetAllPagedAsync(p, s, cancellationToken); // Service implementation.
        return Ok(new { message = ApiMessages.CommentListSuccess, data = result }); // 200.
    }

    // Tất cả comment phẳng của một bài (không phân trang) — đặt sát GET /api/comments để client dễ gọi theo postId.
    [HttpGet("post/{postId:guid}")] // GET .../api/comments/post/{postId}
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllByPostId( // Danh sách đầy đủ một post.
        Guid postId, // Id bài viết.
        CancellationToken cancellationToken = default) // Hủy.
    {
        var result = await _service.GetAllByPostIdAsync(postId, cancellationToken); // Repository GetByPostIdAsync + map.
        return Ok(new { message = ApiMessages.CommentAllByPostSuccess, data = result }); // 200 + mảng CommentDto.
    }

    [HttpGet("search/id/{id:guid}")] // Tìm theo id toàn cục.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> SearchById(Guid id) // Comment id.
    {
        var result = await _service.GetByIdAsync(id); // Một bản ghi.
        return Ok(new { message = ApiMessages.CommentGetSuccess, data = result }); // 200.
    }

    [HttpGet("search/by-content")] // Tìm theo nội dung chứa.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> SearchByContent( // Phân trang.
        [FromQuery] string? content, // Mẫu nội dung.
        [FromQuery] string? page, // Trang.
        [FromQuery] string? pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.SearchByContentPagedAsync(content, p, s, cancellationToken); // DB search.
        return Ok(new { message = ApiMessages.CommentSearchByContentSuccess, data = result }); // 200.
    }

    [HttpGet("all/flat")] // Danh sách phẳng toàn hệ thống (theo service).
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllFlat( // Phân trang.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.GetAllFlatPagedAsync(p, s, cancellationToken); // Flat query.
        return Ok(new { message = ApiMessages.CommentAllFlatSuccess, data = result }); // 200.
    }

    [HttpGet("all/tree/flat")] // Cây nhưng biểu diễn phẳng theo contract service (không flatten DFS).
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllTreeFlat( // Phân trang.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.GetAllTreePagedAsync(p, s, cancellationToken); // Tree paged.
        return Ok(new { message = ApiMessages.CommentAllTreeSuccess, data = result }); // 200.
    }

    [HttpGet("all/cte")] // Toàn cục: danh sách phẳng qua CTE SQL.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllCteFlat( // Phân trang.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.GetAllCteFlatPagedAsync(p, s, cancellationToken); // CTE flat.
        return Ok(new { message = ApiMessages.CommentAllCteFlatSuccess, data = result }); // 200.
    }

    [HttpGet("all/tree/cte")] // Toàn cục: cây materialized qua CTE.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllCteTree( // Phân trang.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.GetAllCteTreePagedAsync(p, s, cancellationToken); // CTE tree.
        return Ok(new { message = ApiMessages.CommentAllCteTreeSuccess, data = result }); // 200.
    }

    // Toàn cục: trang gốc EF → cây → preorder phẳng (không CTE).
    [HttpGet("all/tree/flat/flatten")] // Flatten forest từ EF.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllTreeFlatFlattened( // Phân trang theo dòng phẳng preorder.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.GetFlattenedForestPagedAsync(p, s, cancellationToken); // EF tree flatten.
        return Ok(new { message = ApiMessages.CommentFlattenForestSuccess, data = result }); // 200.
    }

    // Toàn cục: CTE mọi post → cây → preorder; phân trang theo dòng phẳng.
    [HttpGet("tree/cte/flatten")] // CTE flatten toàn hệ.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllCteTreeFlattened( // Phân trang.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.GetFlattenedFromCtePagedAsync(p, s, cancellationToken); // SQL CTE walk.
        return Ok(new { message = ApiMessages.CommentFlattenCteSuccess, data = result }); // 200.
    }

    // Demo phân trang — lazy: mỗi bản ghi tracked, đọc Post/User/Children kích hoạt thêm truy vấn.
    [HttpGet("demo/lazy-loading")] // Endpoint minh họa N+1 / proxy lazy.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetDemoLazyLoadingList( // List demo.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.GetCommentsLazyLoadingDemoPagedAsync(p, s, cancellationToken); // EF lazy pattern.
        return Ok(new { message = ApiMessages.CommentDemoLazyLoadingListSuccess, data = result }); // 200.
    }

    // Demo phân trang — eager: Include Post, User, Parent, Children (AsSplitQuery).
    [HttpGet("demo/eager-loading")] // Eager load demo.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetDemoEagerLoadingList( // List.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.GetCommentsEagerLoadingDemoPagedAsync(p, s, cancellationToken); // Include chain.
        return Ok(new { message = ApiMessages.CommentDemoEagerLoadingListSuccess, data = result }); // 200.
    }

    // Demo phân trang — explicit: sau Skip/Take, LoadAsync từng navigation cho mỗi comment.
    [HttpGet("demo/explicit-loading")] // Explicit loading demo.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetDemoExplicitLoadingList( // List.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.GetCommentsExplicitLoadingDemoPagedAsync(p, s, cancellationToken); // Entry.LoadAsync.
        return Ok(new { message = ApiMessages.CommentDemoExplicitLoadingListSuccess, data = result }); // 200.
    }

    // Demo phân trang — projection: Select DTO trên server (join + COUNT con), không Include.
    [HttpGet("demo/projection")] // Projection-only query.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetDemoProjectionList( // List.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.GetCommentsProjectionDemoPagedAsync(p, s, cancellationToken); // Select DTO.
        return Ok(new { message = ApiMessages.CommentDemoProjectionListSuccess, data = result }); // 200.
    }

    // Demo một comment — lazy loading (proxies).
    [HttpGet("demo/lazy-loading/{id:guid}")] // Chi tiết một id.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetDemoLazyLoading(Guid id, CancellationToken cancellationToken = default) // Single.
    {
        var result = await _service.GetCommentLazyLoadingDemoAsync(id, cancellationToken); // Lazy traverse.
        return Ok(new { message = ApiMessages.CommentDemoLazyLoadingSuccess, data = result }); // 200.
    }

    // Demo một comment — eager (Include / AsSplitQuery).
    [HttpGet("demo/eager-loading/{id:guid}")] // Chi tiết eager.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetDemoEagerLoading(Guid id, CancellationToken cancellationToken = default) // Single.
    {
        var result = await _service.GetCommentEagerLoadingDemoAsync(id, cancellationToken); // Include graph.
        return Ok(new { message = ApiMessages.CommentDemoEagerLoadingSuccess, data = result }); // 200.
    }

    // Demo một comment — explicit (Entry LoadAsync).
    [HttpGet("demo/explicit-loading/{id:guid}")] // Chi tiết explicit.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetDemoExplicitLoading(Guid id, CancellationToken cancellationToken = default) // Single.
    {
        var result = await _service.GetCommentExplicitLoadingDemoAsync(id, cancellationToken); // LoadAsync steps.
        return Ok(new { message = ApiMessages.CommentDemoExplicitLoadingSuccess, data = result }); // 200.
    }

    // Demo một comment — projection (Select SQL, không nạp navigation trên client).
    [HttpGet("demo/projection/{id:guid}")] // Chi tiết projection.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetDemoProjection(Guid id, CancellationToken cancellationToken = default) // Single.
    {
        var result = await _service.GetCommentProjectionDemoAsync(id, cancellationToken); // Single DTO query.
        return Ok(new { message = ApiMessages.CommentDemoProjectionSuccess, data = result }); // 200.
    }

    [HttpGet("{id:guid}")] // GET by id chuẩn.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetById(Guid id) // Id.
    {
        var result = await _service.GetByIdAsync(id); // Standard read.
        return Ok(new { message = ApiMessages.CommentGetSuccess, data = result }); // 200.
    }

    [HttpPost] // Tạo comment (Admin set UserId/PostId trong body).
    [Authorize(Roles = "Admin")] // Chỉ Admin trên route này.
    public async Task<IActionResult> Create([FromBody] CreateCommentDto dto) // Body.
    {
        var result = await _service.CreateAsync(dto); // Insert + integrity checks in service/repo.
        return Ok(new { message = ApiMessages.CommentCreateSuccess, data = result }); // 200 (không 201 — giữ nguyên hành vi).
    }

    // User cập nhật nội dung comment do chính mình; Admin dùng PUT /api/admin/comments/{id} với DTO mở rộng.
    [HttpPut("{id:guid}")] // Author update path.
    [Authorize(Roles = "Admin,User")] // User + Admin nhưng Admin bị redirect logic.
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCommentDto dto) // Chỉ content.
    {
        if (User.IsInRole("Admin")) // Admin không được dùng endpoint tác giả.
        {
            return StatusCode( // 403 JSON.
                StatusCodes.Status403Forbidden, // Forbidden.
                new // Payload.
                {
                    code = ApiErrorCodes.UseAdminUpdateEndpoint, // Code.
                    message = ApiMessages.UseAdminUpdateEndpoint // Message.
                });
        }

        var userId = HttpContextUserId.GetRequiredUserId(User); // JWT user.
        await _service.UpdateAsAuthorAsync(id, dto, userId); // Verify ownership.
        return Ok(new { message = ApiMessages.CommentUpdateSuccess }); // 200.
    }

    // Route tuyệt đối: Admin sửa mọi comment với DTO đầy đủ; service chặn chu trình/sai cây thay vì tách controller.
    [HttpPut("~/api/admin/comments/{id:guid}")] // Admin absolute route.
    [Authorize(Roles = "Admin")] // Admin only.
    public async Task<IActionResult> UpdateAsAdmin(Guid id, [FromBody] AdminUpdateCommentDto dto) // Full rebind fields.
    {
        await _service.UpdateAsAdminAsync(id, dto); // Validation tree in service.
        return Ok(new { message = ApiMessages.CommentUpdateSuccess }); // 200.
    }

    [HttpDelete("{id:guid}")] // Xóa comment.
    [Authorize(Roles = "Admin")] // Admin only.
    public async Task<IActionResult> Delete(Guid id) // Id.
    {
        await _service.DeleteAsync(id); // Delete.
        return Ok(new { message = ApiMessages.CommentDeleteSuccess }); // 200.
    }

    // Tìm một comment theo id trong phạm vi một post.
    [HttpGet("post/{postId:guid}/search/id/{commentId:guid}")] // Scoped search.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> SearchByIdInPost(Guid postId, Guid commentId, CancellationToken cancellationToken = default) // Two ids.
    {
        var result = await _service.GetByIdInPostAsync(postId, commentId, cancellationToken); // Must belong to post.
        return Ok(new { message = ApiMessages.CommentSearchByIdInPostSuccess, data = result }); // 200.
    }

    // Tìm theo nội dung chỉ trong một post (phân trang).
    [HttpGet("post/{postId:guid}/search/by-content")] // Scoped content search.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> SearchByContentInPost( // Params.
        Guid postId, // Post scope.
        [FromQuery] string? content, // Needle.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.SearchByContentInPostPagedAsync(postId, content, p, s, cancellationToken); // Query.
        return Ok(new { message = ApiMessages.CommentSearchByContentInPostSuccess, data = result }); // 200.
    }

    [HttpGet("post/{postId:guid}/flat")] // Danh sách phẳng theo post.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetFlatByPostId( // Phân trang.
        Guid postId, // Post.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.GetFlatByPostIdPagedAsync(postId, p, s, cancellationToken); // EF/SQL flat.
        return Ok(new { message = ApiMessages.CommentFlatByPostSuccess, data = result }); // 200.
    }

    [HttpGet("post/{postId:guid}/cte")] // Phẳng CTE theo post.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetCteFlatByPostId( // Phân trang.
        Guid postId, // Post.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.GetCteFlatByPostIdPagedAsync(postId, p, s, cancellationToken); // CTE.
        return Ok(new { message = ApiMessages.CommentCteFlatByPostSuccess, data = result }); // 200.
    }

    [HttpGet("post/{postId:guid}/tree/flat")] // Cây (flat representation) theo post.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetTreeByPostId( // Phân trang.
        Guid postId, // Post.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.GetTreeByPostIdPagedAsync(postId, p, s, cancellationToken); // Tree.
        return Ok(new { message = ApiMessages.CommentTreeByPostSuccess, data = result }); // 200.
    }

    [HttpGet("post/{postId:guid}/tree/cte")] // Cây CTE theo post.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetCteTreeByPostId( // Phân trang.
        Guid postId, // Post.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.GetCteTreeByPostIdPagedAsync(postId, p, s, cancellationToken); // CTE tree.
        return Ok(new { message = ApiMessages.CommentCteTreeByPostSuccess, data = result }); // 200.
    }

    // CTE một post → cây → danh sách phẳng preorder (khác .../cte là thứ tự/Level theo DFS).
    [HttpGet("post/{postId:guid}/tree/cte/flatten")] // Flatten CTE per post.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetFlattenedCteTreeByPostId( // Phân trang.
        Guid postId, // Post.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.GetFlattenedCteTreeByPostIdPagedAsync(postId, p, s, cancellationToken); // Flatten.
        return Ok(new { message = ApiMessages.CommentFlattenCteTreeByPostSuccess, data = result }); // 200.
    }

    [HttpGet("post/{postId:guid}/tree/flat/flatten")] // EF flatten tree per post.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetTreeFlattenByPostId( // Phân trang.
        Guid postId, // Post.
        [FromQuery] string? page, // Page.
        [FromQuery] string? pageSize, // Size.
        CancellationToken cancellationToken = default) // CT.
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var result = await _service.GetFlattenedTreeByPostIdPagedAsync(postId, p, s, cancellationToken); // EF walk.
        return Ok(new { message = ApiMessages.CommentFlattenTreeByPostSuccess, data = result }); // 200.
    }
}
