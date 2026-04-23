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

    // GET /api/comments — luôn phân trang (page/pageSize); một comment theo id dùng GET /api/comments/{id}.
    [HttpGet] // postId = khóa bài viết (Post).
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAll( // Bộ lọc query thống nhất.
        [FromQuery] Guid? postId, // Lọc theo Id bài viết (Post), không phải Id comment.
        [FromQuery] string? content, // Tìm Contains trong nội dung comment.
        [FromQuery] string? page, // Số trang.
        [FromQuery] string? pageSize, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Biên dưới CreatedAt (inclusive).
        [FromQuery] DateTime? createdAtTo = null, // Biên trên CreatedAt (inclusive).
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu from > to.
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Luôn bật phân trang.
        var result = await _service.GetCommentListAsync(postId, content, false, p, s, cancellationToken, createdAtFrom, createdAtTo); // Không unpaged từ route này.
        return Ok(new { message = ApiMessages.CommentListSuccess, data = result }); // 200 + PagedResult<CommentDto>.
    } // Kết thúc GetAll.

    [HttpGet("{id:guid}")] // GET by id chuẩn.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetById(Guid id) // Id.
    {
        var result = await _service.GetByIdAsync(id); // Standard read.
        return Ok(new { message = ApiMessages.CommentGetSuccess, data = result }); // 200.
    }

    // Danh sách comment do một user tạo — literal "user" tránh trùng template GET /api/comments/{id}.
    [HttpGet("user/{userId:guid}")] // Cạnh route theo id về mặt REST; route cụ thể hơn {id}.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetByUserId( // Phân trang theo tác giả.
        Guid userId, // UserId (không phải Comment.Id).
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Chuẩn hóa phân trang.
        var result = await _service.GetCommentsByUserIdPagedAsync(userId, p, s, cancellationToken, createdAtFrom, createdAtTo); // 404 nếu user không tồn tại.
        return Ok(new { message = ApiMessages.CommentListByUserSuccess, data = result }); // 200.
    } // Kết thúc GetByUserId.

    [HttpPost] // Tạo comment
    [Authorize(Roles = "Admin,User")] 
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

    // Danh sách phẳng CommentDto — luôn phân trang; ?postId= là Id bài viết (Post).
    [HttpGet("flat")] // Dữ liệu “thô” phẳng giống list chuẩn.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllFlat( // Chỉ phân trang.
        [FromQuery] Guid? postId, // Tuỳ chọn: Id bài viết (Post), không phải Id comment.
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Luôn phân trang.
        if (postId is { } pid) // Phân trang trong một bài.
        { // Mở khối.
            var byPost = await _service.GetFlatByPostIdPagedAsync(pid, p, s, cancellationToken, createdAtFrom, createdAtTo); // EF phân trang theo post.
            return Ok(new { message = ApiMessages.CommentFlatByPostSuccess, data = byPost }); // 200.
        } // Kết thúc theo post.

        var global = await _service.GetAllFlatPagedAsync(p, s, cancellationToken, createdAtFrom, createdAtTo); // Toàn hệ có phân trang.
        return Ok(new { message = ApiMessages.CommentAllFlatSuccess, data = global }); // 200.
    } // Kết thúc GetAllFlat.

    // Phẳng có Level — hàng thô từ file SQL CTE (CommentTree_*.sql); luôn phân trang; ?postId= là Id bài viết (Post).
    [HttpGet("cte")] // Khác route flat: flat = EF, cte = ADO CTE.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllCteFlat( // Flat + Level.
        [FromQuery] Guid? postId, // Tuỳ chọn: Id bài viết (Post), không phải Id comment.
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Luôn phân trang.
        if (postId is { } pid) // Phân trang trong post.
        { // Mở khối.
            var pageRows = await _service.GetCteFlatByPostIdPagedAsync(pid, p, s, cancellationToken, createdAtFrom, createdAtTo); // Trang CommentFlatDto.
            return Ok(new { message = ApiMessages.CommentCteFlatByPostSuccess, data = pageRows }); // 200.
        } // Kết thúc post.

        var global = await _service.GetAllCteFlatPagedAsync(p, s, cancellationToken, createdAtFrom, createdAtTo); // Trang toàn hệ.
        return Ok(new { message = ApiMessages.CommentAllCteFlatSuccess, data = global }); // 200.
    } // Kết thúc GetAllCteFlat.

    // Cây (biểu diễn phẳng theo DTO cây): ?postId= là Id bài viết (Post).
    [HttpGet("tree/flat")] // Phân trang theo gốc (roots).
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllTreeFlat( // Tree paged.
        [FromQuery] Guid? postId, // Id bài viết (Post); bỏ trống = toàn hệ thống.
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Luôn phân trang (theo gốc).
        if (postId is { } pid) // Cây trong một post.
        { // Mở khối.
            var trees = await _service.GetTreeByPostIdPagedAsync(pid, p, s, cancellationToken, createdAtFrom, createdAtTo); // EF tree + phân trang gốc.
            return Ok(new { message = ApiMessages.CommentTreeByPostSuccess, data = trees }); // 200.
        } // Kết thúc post.

        var allTrees = await _service.GetAllTreePagedAsync(p, s, cancellationToken, createdAtFrom, createdAtTo); // Rừng gốc toàn hệ thống.
        return Ok(new { message = ApiMessages.CommentAllTreeSuccess, data = allTrees }); // 200.
    } // Kết thúc GetAllTreeFlat.

    // Cây lồng từ hàng CTE (sau đó dựng cây RAM); khác tree/flat (EF). ?postId= là Id bài viết (Post).
    [HttpGet("tree/cte")] // Service: GetTreeRowsByCte* → BuildTreeFromFlatDtosForOnePost / rừng toàn cục.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllCteTree( // Tree paged.
        [FromQuery] Guid? postId, // Id bài viết (Post); bỏ trống = toàn hệ.
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Phân trang bắt buộc.
        if (postId is { } pid) // Một post.
        { // Mở khối.
            var trees = await _service.GetCteTreeByPostIdPagedAsync(pid, p, s, cancellationToken, createdAtFrom, createdAtTo); // Cây từ CTE + phân trang gốc.
            return Ok(new { message = ApiMessages.CommentCteTreeByPostSuccess, data = trees }); // 200.
        } // Kết thúc post.

        var allTrees = await _service.GetAllCteTreePagedAsync(p, s, cancellationToken, createdAtFrom, createdAtTo); // Toàn hệ.
        return Ok(new { message = ApiMessages.CommentAllCteTreeSuccess, data = allTrees }); // 200.
    } // Kết thúc GetAllCteTree.

    // EF: gốc → cây → preorder phẳng; ?postId= là Id bài viết (Post).
    [HttpGet("tree/flat/flatten")] // Flatten rừng EF.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllTreeFlatFlattened( // Phân trang trên dòng phẳng (theo trang gốc).
        [FromQuery] Guid? postId, // Id bài viết (Post); bỏ trống = toàn hệ.
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        if (postId is { } pid) // Một bài: làm phẳng cây EF trong post.
        { // Mở khối.
            var flat = await _service.GetFlattenedTreeByPostIdPagedAsync(pid, p, s, cancellationToken, createdAtFrom, createdAtTo); // Preorder + Level.
            return Ok(new { message = ApiMessages.CommentFlattenTreeByPostSuccess, data = flat }); // 200.
        } // Kết thúc post.

        var forest = await _service.GetFlattenedForestPagedAsync(p, s, cancellationToken, createdAtFrom, createdAtTo); // Toàn hệ: trang gốc.
        return Ok(new { message = ApiMessages.CommentFlattenForestSuccess, data = forest }); // 200.
    } // Kết thúc GetAllTreeFlatFlattened.

    // CTE toàn cục hoặc một post → preorder phẳng; ?postId= là Id bài viết (Post).
    [HttpGet("tree/cte/flatten")] // Flatten sau CTE.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllCteTreeFlattened( // Phân trang trên dòng phẳng.
        [FromQuery] Guid? postId, // Id bài viết (Post); bỏ trống = mọi bài.
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        if (postId is { } pid) // CTE + flatten trong một post.
        { // Mở khối.
            var flat = await _service.GetFlattenedCteTreeByPostIdPagedAsync(pid, p, s, cancellationToken, createdAtFrom, createdAtTo); // Cache khi không lọc ngày.
            return Ok(new { message = ApiMessages.CommentFlattenCteTreeByPostSuccess, data = flat }); // 200.
        } // Kết thúc post.

        var global = await _service.GetFlattenedFromCtePagedAsync(p, s, cancellationToken, null, createdAtFrom, createdAtTo); // Mọi post — postId null.
        return Ok(new { message = ApiMessages.CommentFlattenCteSuccess, data = global }); // 200.
    } // Kết thúc GetAllCteTreeFlattened.

    // Demo lazy: chỉ danh sách (nhiều comment) — filterByPostId + paginationEnabled; không nhận id comment.
    [HttpGet("demo/lazy-loading")] // So sánh lazy navigation trên tập bản ghi.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetDemoLazyLoadingList( // List — mục đích tải nhiều comment.
        [FromQuery(Name = "filterByPostId")] Guid? filterByPostId, // Id bài viết (Post), tuỳ chọn.
        [FromQuery] bool paginationEnabled = true, // false = trả toàn bộ dòng khớp filter (cẩn thận kích thước).
        [FromQuery] string? page = null, // Trang khi paginationEnabled=true.
        [FromQuery] string? pageSize = null, // Cỡ trang khi paginationEnabled=true.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var (unpaged, p, s) = PaginationQuery.ParsePaginationFromQuery(page, pageSize, paginationEnabled); // Chỉ demo dùng cờ này.
        if (unpaged) // Không Skip/Take.
        { // Mở khối.
            var result = await _service.GetAllCommentsLazyLoadingDemoAsync(cancellationToken, filterByPostId, createdAtFrom, createdAtTo); // SELECT đủ + lazy nav.
            return Ok(new { message = ApiMessages.CommentDemoLazyLoadingAllSuccess, data = result, totalCount = result.Count }); // 200.
        } // Kết thúc unpaged.

        var paged = await _service.GetCommentsLazyLoadingDemoPagedAsync(p, s, cancellationToken, filterByPostId, createdAtFrom, createdAtTo); // Trang + lazy.
        return Ok(new { message = ApiMessages.CommentDemoLazyLoadingListSuccess, data = paged }); // 200.
    } // Kết thúc GetDemoLazyLoadingList.

    // Demo eager: chỉ danh sách — Include/split query trên nhiều comment.
    [HttpGet("demo/eager-loading")] // List.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetDemoEagerLoadingList( // List.
        [FromQuery(Name = "filterByPostId")] Guid? filterByPostId, // Id Post — tuỳ chọn.
        [FromQuery] bool paginationEnabled = true, // false = toàn bộ khớp lọc.
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var (unpaged, p, s) = PaginationQuery.ParsePaginationFromQuery(page, pageSize, paginationEnabled); // Parse.
        if (unpaged) // Include toàn tập khớp.
        { // Mở khối.
            var result = await _service.GetAllCommentsEagerLoadingDemoAsync(cancellationToken, filterByPostId, createdAtFrom, createdAtTo); // Split query + Include.
            return Ok(new { message = ApiMessages.CommentDemoEagerLoadingAllSuccess, data = result, totalCount = result.Count }); // 200.
        } // Kết thúc unpaged.

        var paged = await _service.GetCommentsEagerLoadingDemoPagedAsync(p, s, cancellationToken, filterByPostId, createdAtFrom, createdAtTo); // Trang eager.
        return Ok(new { message = ApiMessages.CommentDemoEagerLoadingListSuccess, data = paged }); // 200.
    } // Kết thúc GetDemoEagerLoadingList.

    // Demo explicit: chỉ danh sách — LoadAsync từng bước trên nhiều comment.
    [HttpGet("demo/explicit-loading")] // List.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetDemoExplicitLoadingList( // List.
        [FromQuery(Name = "filterByPostId")] Guid? filterByPostId, // Id Post — tuỳ chọn.
        [FromQuery] bool paginationEnabled = true, // false = không phân trang.
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var (unpaged, p, s) = PaginationQuery.ParsePaginationFromQuery(page, pageSize, paginationEnabled); // Parse.
        if (unpaged) // Mọi dòng khớp + LoadAsync.
        { // Mở khối.
            var result = await _service.GetAllCommentsExplicitLoadingDemoAsync(cancellationToken, filterByPostId, createdAtFrom, createdAtTo); // Nhiều câu SQL nhỏ.
            return Ok(new { message = ApiMessages.CommentDemoExplicitLoadingAllSuccess, data = result, totalCount = result.Count }); // 200.
        } // Kết thúc unpaged.

        var paged = await _service.GetCommentsExplicitLoadingDemoPagedAsync(p, s, cancellationToken, filterByPostId, createdAtFrom, createdAtTo); // Trang explicit.
        return Ok(new { message = ApiMessages.CommentDemoExplicitLoadingListSuccess, data = paged }); // 200.
    } // Kết thúc GetDemoExplicitLoadingList.

    // Demo projection: chỉ danh sách — Select DTO trên nhiều comment (không Include graph đầy đủ).
    [HttpGet("demo/projection")] // List.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetDemoProjectionList( // List.
        [FromQuery(Name = "filterByPostId")] Guid? filterByPostId, // Id Post — tuỳ chọn.
        [FromQuery] bool paginationEnabled = true, // false = không phân trang.
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var (unpaged, p, s) = PaginationQuery.ParsePaginationFromQuery(page, pageSize, paginationEnabled); // Parse.
        if (unpaged) // ToList projection một pipeline.
        { // Mở khối.
            var result = await _service.GetAllCommentsProjectionDemoAsync(cancellationToken, filterByPostId, createdAtFrom, createdAtTo); // Không Include graph.
            return Ok(new { message = ApiMessages.CommentDemoProjectionAllSuccess, data = result, totalCount = result.Count }); // 200.
        } // Kết thúc unpaged.

        var paged = await _service.GetCommentsProjectionDemoPagedAsync(p, s, cancellationToken, filterByPostId, createdAtFrom, createdAtTo); // Trang projection.
        return Ok(new { message = ApiMessages.CommentDemoProjectionListSuccess, data = paged }); // 200.
    } // Kết thúc GetDemoProjectionList.

}
