using CommentAPI; // ApiException, PaginationQuery, CreatedAtRangeQuery dùng chung.
using Asp.Versioning; // [ApiVersion] gắn phiên bản 2.0 vào controller.
using CommentAPI.Controllers; // HttpContextUserId lấy user id bắt buộc khi cần.
using CommentAPI.DTOs; // CreateCommentDto, UpdateCommentDto, AdminUpdateCommentDto, phân trang.
using CommentAPI.Interfaces; // ICommentService, ICommentRepository.
using CommentAPI.Validators; // FluentValidation cho body comment.
using Microsoft.AspNetCore.Authorization; // [Authorize(Roles = "...")].
using Microsoft.AspNetCore.Http; // StatusCodes trong một số phản hồi lỗi có chủ đích.
using Microsoft.AspNetCore.Mvc; // ControllerBase, IActionResult, FromQuery/FromBody.
namespace CommentAPI.V2.Controllers; 

[ApiController] // Web API.
[ApiVersion("2.0")] // Phiên bản 2.0 trong URL.

[Authorize] // JWT.
[Route("api/v{version:apiVersion}/comments")] // Base path có version.
public class CommentsController : ControllerBase // JSON only.
{
    private readonly ICommentService _service; // Service comment phức tạp (cây, CTE, demo loading).
    private readonly ICommentRepository _commentRepository; // Parse sort/sortDir whitelist theo repository.

    public CommentsController(ICommentService service, ICommentRepository commentRepository) // Constructor: inject service + repository parse.
    { // Mở khối gán phụ thuộc cho controller.
        _service = service; // Lưu service để mọi action gọi nghiệp vụ comment (CRUD, cây, demo).
        _commentRepository = commentRepository; // Parse tham số sort an toàn (cột + hướng).
    } // Kết thúc constructor.

    // [1] GET /api/comments — luôn phân trang (page/pageSize); một comment theo id dùng GET /api/comments/{id}.
    [HttpGet] // postId = khóa bài viết (Post).
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAll( // Bộ lọc query thống nhất.
        [FromQuery] Guid? postId, // Lọc theo Id bài viết (Post), không phải Id comment.
        [FromQuery] Guid? userId, // Lọc theo Id tác giả (UserId).
        [FromQuery] string? content, // Tìm Contains trong nội dung comment.
        [FromQuery] string? page, // Số trang.
        [FromQuery] string? pageSize, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Biên dưới CreatedAt (inclusive).
        [FromQuery] DateTime? createdAtTo = null, // Biên trên CreatedAt (inclusive).
        [FromQuery] string? sort = null, // Cột sort (tên cột JSON/legacy By* / 0..4).
        [FromQuery] string? sortDir = null, // asc (mặc định) hoặc desc.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu from > to.
        var sortSpec = _commentRepository.ParseCommentListSortOrThrow(sort, sortDir);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Luôn bật phân trang.
        var result = await _service.GetCommentListAsync(postId, content, false, p, s, cancellationToken, createdAtFrom, createdAtTo, userId, sortSpec); // Không unpaged từ route này.
        return Ok(new { message = ApiMessages.CommentListSuccess, data = result }); // 200 + PagedResult<CommentDto>.
    } // Kết thúc GetAll.

    // [2] GET /api/comments/{id}
    [HttpGet("{id:guid}")] // GET by id chuẩn.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetById(Guid id) // Id comment trên URL.
    { // Mở action GetById.
        var result = await _service.GetByIdAsync(id); // Đọc một comment (cache + DB); ném ApiException 404 nếu không có.
        return Ok(new { message = ApiMessages.CommentGetSuccess, data = result }); // 200 kèm envelope message + DTO.
    } // Kết thúc GetById.

    // [3] GET /api/comments/user/{userId}
    // Danh sách comment do một user tạo — literal "user" tránh trùng template GET /api/comments/{id}.
    [HttpGet("user/{userId:guid}")] // Cạnh route theo id về mặt REST; route cụ thể hơn {id}.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetByUserId( // Phân trang theo tác giả.
        Guid userId, // UserId (không phải Comment.Id).
        [FromQuery] string? content = null, // Tìm Contains trong nội dung (tuỳ chọn).
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        [FromQuery] string? sort = null, // Cột sort.
        [FromQuery] string? sortDir = null, // asc hoặc desc.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var sortSpec = _commentRepository.ParseCommentListSortOrThrow(sort, sortDir); // 400 nếu sort/sortDir sai.
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Chuẩn hóa phân trang.
        var result = await _service.GetCommentsByUserIdPagedAsync(userId, p, s, cancellationToken, createdAtFrom, createdAtTo, content, sortSpec); // 404 nếu user không tồn tại.
        return Ok(new { message = ApiMessages.CommentListByUserSuccess, data = result }); // 200.
    } // Kết thúc GetByUserId.

    // [4] POST /api/comments
    [HttpPost] // Tạo comment
    [Authorize(Roles = "Admin,User")] 
    public async Task<IActionResult> Create([FromBody] CreateCommentDto dto) // Body JSON tạo comment.
    { // Mở action Create.
        var result = await _service.CreateAsync(dto); // Kiểm tra post/user/parent rồi INSERT; trả DTO sau khi gán Id.
        return Ok(new { message = ApiMessages.CommentCreateSuccess, data = result }); // 200 + message + dữ liệu (hành vi API giữ 200 thay vì 201).
    } // Kết thúc Create.

    // [5] PUT /api/comments/{id}
    // User cập nhật nội dung comment do chính mình; Admin dùng PUT /api/admin/comments/{id} với DTO mở rộng.
    [HttpPut("{id:guid}")] // Author update path.
    [Authorize(Roles = "Admin,User")] // User + Admin nhưng Admin bị redirect logic.
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCommentDto dto) // Cập nhật nội dung theo quyền tác giả.
    { // Mở action Update (user route).
        if (User.IsInRole("Admin")) // Admin phải dùng route admin riêng, không được sửa qua endpoint tác giả.
        { // Mở nhánh từ chối.
            return StatusCode( // Trả mã HTTP tùy chỉnh kèm object lỗi.
                StatusCodes.Status403Forbidden, // 403 Forbidden — sai endpoint cho vai trò Admin.
                new // Đối tượng vô danh chứa code + message cho client.
                {
                    code = ApiErrorCodes.UseAdminUpdateEndpoint, // Mã lỗi nghiệp vụ thống nhất.
                    message = ApiMessages.UseAdminUpdateEndpoint // Thông điệp hướng dẫn dùng PUT admin.
                }); // Kết thúc StatusCode.
        } // Kết thúc nhánh Admin.

        var userId = HttpContextUserId.GetRequiredUserId(User); // Lấy Guid user từ claim JWT (bắt buộc có).
        await _service.UpdateAsAuthorAsync(id, dto, userId); // Chỉ cho phép nếu UserId comment trùng JWT; xóa cache chi tiết.
        return Ok(new { message = ApiMessages.CommentUpdateSuccess }); // 200 chỉ message (không trả body comment).
    } // Kết thúc Update.

    // [6] PUT /api/admin/comments/{id}
    // Route tuyệt đối: Admin sửa mọi comment với DTO đầy đủ; service chặn chu trình/sai cây thay vì tách controller.
    [HttpPut("~/api/v{version:apiVersion}/admin/comments/{id:guid}")] // Admin absolute route có segment version.
    [Authorize(Roles = "Admin")] // Admin only.
    public async Task<IActionResult> UpdateAsAdmin(Guid id, [FromBody] AdminUpdateCommentDto dto) // Payload đầy đủ trường cho Admin.
    { // Mở action UpdateAsAdmin.
        await _service.UpdateAsAdminAsync(id, dto); // Kiểm tra chu trình/parent/post; có thể đổi PostId cả subtree; xóa cache liên quan.
        return Ok(new { message = ApiMessages.CommentUpdateSuccess }); // 200 thành công.
    } // Kết thúc UpdateAsAdmin.

    // [7] DELETE /api/comments/{id}
    [HttpDelete("{id:guid}")] // Xóa comment.
    [Authorize(Roles = "Admin")] // Admin only.
    public async Task<IActionResult> Delete(Guid id) // Id comment cần xóa (kèm subtree trong cùng post).
    { // Mở action Delete.
        await _service.DeleteAsync(id); // BFS subtree trong post rồi Remove + SaveChanges; xóa cache các Id đã xóa.
        return Ok(new { message = ApiMessages.CommentDeleteSuccess }); // 200 báo thành công.
    } // Kết thúc Delete.

    // [8] GET /api/comments/flat
    // Danh sách phẳng CommentDto — luôn phân trang; ?postId= là Id bài viết (Post).
    [HttpGet("flat")] // Dữ liệu “thô” phẳng giống list chuẩn.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllFlat( // Chỉ phân trang.
        [FromQuery] Guid? postId, // Tuỳ chọn: Id bài viết (Post), không phải Id comment.
        [FromQuery] Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        [FromQuery] string? content = null, // Tìm trong nội dung (tuỳ chọn).
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        [FromQuery] string? sort = null, // Cột sort.
        [FromQuery] string? sortDir = null, // asc hoặc desc.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var sortSpec = _commentRepository.ParseCommentListSortOrThrow(sort, sortDir); // 400 nếu sort/sortDir sai.
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Luôn phân trang.
        var data = await _service.GetFlatRoutePagedAsync(postId, p, s, cancellationToken, createdAtFrom, createdAtTo, userId, content, sortSpec); // Một hàm service cho toàn route.
        var message = postId is { } ? ApiMessages.CommentFlatByPostSuccess : ApiMessages.CommentAllFlatSuccess;
        return Ok(new { message, data }); // 200.
    } // Kết thúc GetAllFlat.

    // [9] GET /api/comments/cte
    // Phẳng có Level — hàng thô từ file SQL CTE (CommentTree_*.sql); luôn phân trang; ?postId= là Id bài viết (Post).
    [HttpGet("cte")] // Khác /flat: đây là danh sách phẳng + Level từ một câu SQL (CTE); /flat đọc bảng Comment trong ứng dụng rồi ánh xạ DTO.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllCteFlat( // Flat + Level.
        [FromQuery] Guid? postId, // Tuỳ chọn: Id bài viết (Post), không phải Id comment.
        [FromQuery] Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        [FromQuery] string? content = null, // Tìm trong nội dung (tuỳ chọn).
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        [FromQuery] string? sort = null, // Cột sort.
        [FromQuery] string? sortDir = null, // asc hoặc desc.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var sortSpec = _commentRepository.ParseCommentListSortOrThrow(sort, sortDir); // 400 nếu sort/sortDir sai.
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Luôn phân trang.
        var data = await _service.GetCteFlatRoutePagedAsync(postId, p, s, cancellationToken, createdAtFrom, createdAtTo, userId, content, sortSpec); // Một hàm service cho toàn route.
        var message = postId is { } ? ApiMessages.CommentCteFlatByPostSuccess : ApiMessages.CommentAllCteFlatSuccess;
        return Ok(new { message, data }); // 200.
    } // Kết thúc GetAllCteFlat.

    // [10] GET /api/comments/tree/flat
    // Cây (biểu diễn phẳng theo DTO cây): ?postId= là Id bài viết (Post).
    [HttpGet("tree/flat")] // Phân trang theo gốc (roots).
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllTreeFlat( // Tree paged.
        [FromQuery] Guid? postId, // Id bài viết (Post); bỏ trống = toàn hệ thống.
        [FromQuery] Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        [FromQuery] string? content = null, // Tìm trong nội dung (tuỳ chọn).
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        [FromQuery] string? sort = null, // Cột sort.
        [FromQuery] string? sortDir = null, // asc hoặc desc.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var sortSpec = _commentRepository.ParseCommentListSortOrThrow(sort, sortDir); // 400 nếu sort/sortDir sai.
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Luôn phân trang (theo gốc).
        var data = await _service.GetTreeFlatRoutePagedAsync(postId, p, s, cancellationToken, createdAtFrom, createdAtTo, userId, content, sortSpec); // Một hàm service cho toàn route.
        var message = postId is { } ? ApiMessages.CommentTreeByPostSuccess : ApiMessages.CommentAllTreeSuccess;
        return Ok(new { message, data }); // 200.
    } // Kết thúc GetAllTreeFlat.

    // [11] GET /api/comments/tree/cte
    // Cây lồng từ hàng CTE (sau đó dựng cây RAM); khác GET tree/flat — tree/flat dựng cây sau khi đã nạp hàng từ bảng Comment. ?postId= là Id bài viết (Post).
    [HttpGet("tree/cte")] // Service: GetTreeRowsByCte* → BuildTreeFromFlatDtosForOnePost / rừng toàn cục.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllCteTree( // Tree paged.
        [FromQuery] Guid? postId, // Id bài viết (Post); bỏ trống = toàn hệ.
        [FromQuery] Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        [FromQuery] string? content = null, // Tìm trong nội dung (tuỳ chọn).
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        [FromQuery] string? sort = null, // Cột sort.
        [FromQuery] string? sortDir = null, // asc hoặc desc.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var sortSpec = _commentRepository.ParseCommentListSortOrThrow(sort, sortDir); // 400 nếu sort/sortDir sai.
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Phân trang bắt buộc.
        var data = await _service.GetTreeCteRoutePagedAsync(postId, p, s, cancellationToken, createdAtFrom, createdAtTo, userId, content, sortSpec); // Một hàm service cho toàn route.
        var message = postId is { } ? ApiMessages.CommentCteTreeByPostSuccess : ApiMessages.CommentAllCteTreeSuccess;
        return Ok(new { message, data }); // 200.
    } // Kết thúc GetAllCteTree.

    // [12] GET /api/comments/tree/flat/flatten
    // Phân trang theo gốc (pageSize = số thread) → mỗi gốc nạp đủ subtree → duyệt preorder thành danh sách phẳng (số dòng có thể > pageSize).
    [HttpGet("tree/flat/flatten")] // Làm phẳng rừng từ route tree/flat.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllTreeFlatFlattened( // Metadata totalPages theo gốc; body là preorder các thread của trang.
        [FromQuery] Guid? postId, // Id bài viết (Post); bỏ trống = toàn hệ.
        [FromQuery] Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        [FromQuery] string? content = null, // Tìm trong nội dung (tuỳ chọn).
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        [FromQuery] string? sort = null, // Cột sort.
        [FromQuery] string? sortDir = null, // asc hoặc desc.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var sortSpec = _commentRepository.ParseCommentListSortOrThrow(sort, sortDir); // 400 nếu sort/sortDir sai.
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var data = await _service.GetTreeFlatFlattenRoutePagedAsync(postId, p, s, cancellationToken, createdAtFrom, createdAtTo, userId, content, sortSpec); // Một hàm service cho toàn route.
        var message = postId is { } ? ApiMessages.CommentFlattenTreeByPostSuccess : ApiMessages.CommentFlattenForestSuccess;
        return Ok(new { message, data }); // 200.
    } // Kết thúc GetAllTreeFlatFlattened.

    // [13] GET /api/comments/tree/cte/flatten
    // CTE toàn cục hoặc một post → preorder phẳng; ?postId= là Id bài viết (Post).
    [HttpGet("tree/cte/flatten")] // Flatten sau CTE.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetAllCteTreeFlattened( // Phân trang trên dòng phẳng.
        [FromQuery] Guid? postId, // Id bài viết (Post); bỏ trống = mọi bài.
        [FromQuery] Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        [FromQuery] string? content = null, // Tìm trong nội dung (tuỳ chọn).
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        [FromQuery] string? sort = null, // Cột sort.
        [FromQuery] string? sortDir = null, // asc hoặc desc.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var sortSpec = _commentRepository.ParseCommentListSortOrThrow(sort, sortDir); // 400 nếu sort/sortDir sai.
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var data = await _service.GetTreeCteFlattenRoutePagedAsync(postId, p, s, cancellationToken, createdAtFrom, createdAtTo, userId, content, sortSpec); // Một hàm service cho toàn route.
        var message = postId is { } ? ApiMessages.CommentFlattenCteTreeByPostSuccess : ApiMessages.CommentFlattenCteSuccess;
        return Ok(new { message, data }); // 200.
    } // Kết thúc GetAllCteTreeFlattened.

    // [13b] GET /api/comments/flatten
    // CTE toàn cục hoặc một post → preorder phẳng; ?postId= là Id bài viết (Post).
    [HttpGet("flatten")] // Flatten sau CTE.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetCteTreeFlattened( // Phân trang trên dòng phẳng.
        [FromQuery] Guid? postId, // Id bài viết (Post); bỏ trống = mọi bài.
        [FromQuery] Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        [FromQuery] string? content = null, // Tìm trong nội dung (tuỳ chọn).
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        [FromQuery] string? sort = null, // Cột sort.
        [FromQuery] string? sortDir = null, // asc hoặc desc.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var sortSpec = _commentRepository.ParseCommentListSortOrThrow(sort, sortDir); // 400 nếu sort/sortDir sai.
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize); // Parse.
        var data = await _service.GetTreeCteFlattenRoutePagedAsync(postId, p, s, cancellationToken, createdAtFrom, createdAtTo, userId, content, sortSpec); // Một hàm service cho toàn route.
        var message = postId is { } ? ApiMessages.CommentFlattenCteTreeByPostSuccess : ApiMessages.CommentFlattenCteSuccess;
        return Ok(new { message, data }); // 200.
    } // Kết thúc GetAllCteTreeFlattened.

    // [14] GET /api/comments/demo/lazy-loading
    // Demo lazy: chỉ danh sách (nhiều comment) — filterByPostId + paginationEnabled; không nhận id comment.
    [HttpGet("demo/lazy-loading")] // So sánh lazy navigation trên tập bản ghi.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetDemoLazyLoadingList( // List — mục đích tải nhiều comment.
        [FromQuery(Name = "filterByPostId")] Guid? filterByPostId, // Id bài viết (Post), tuỳ chọn.
        [FromQuery] Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        [FromQuery] string? content = null, // Tìm trong nội dung (tuỳ chọn).
        [FromQuery] bool paginationEnabled = true, // false = trả toàn bộ dòng khớp filter (cẩn thận kích thước).
        [FromQuery] string? page = null, // Trang khi paginationEnabled=true.
        [FromQuery] string? pageSize = null, // Cỡ trang khi paginationEnabled=true.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        [FromQuery] string? sort = null, // Cột sort.
        [FromQuery] string? sortDir = null, // asc hoặc desc.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var sortSpec = _commentRepository.ParseCommentListSortOrThrow(sort, sortDir); // 400 nếu sort/sortDir sai.
        var (unpaged, p, s) = PaginationQuery.ParsePaginationFromQuery(page, pageSize, paginationEnabled); // Chỉ demo dùng cờ này.
        if (unpaged) // Không Skip/Take.
        { // Mở khối.
            var result = await _service.GetAllCommentsLazyLoadingDemoAsync(cancellationToken, filterByPostId, createdAtFrom, createdAtTo, userId, content, sortSpec); // SELECT đủ + lazy nav.
            return Ok(new { message = ApiMessages.CommentDemoLazyLoadingAllSuccess, data = result, totalCount = result.Count }); // 200.
        } // Kết thúc unpaged.

        var paged = await _service.GetCommentsLazyLoadingDemoPagedAsync(p, s, cancellationToken, filterByPostId, createdAtFrom, createdAtTo, userId, content, sortSpec); // Trang + lazy.
        return Ok(new { message = ApiMessages.CommentDemoLazyLoadingListSuccess, data = paged }); // 200.
    } // Kết thúc GetDemoLazyLoadingList.

    // [15] GET /api/comments/demo/eager-loading
    // Demo eager: chỉ danh sách — Include/split query trên nhiều comment.
    [HttpGet("demo/eager-loading")] // List.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetDemoEagerLoadingList( // List.
        [FromQuery(Name = "filterByPostId")] Guid? filterByPostId, // Id Post — tuỳ chọn.
        [FromQuery] Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        [FromQuery] string? content = null, // Tìm trong nội dung (tuỳ chọn).
        [FromQuery] bool paginationEnabled = true, // false = toàn bộ khớp lọc.
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        [FromQuery] string? sort = null, // Cột sort.
        [FromQuery] string? sortDir = null, // asc hoặc desc.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var sortSpec = _commentRepository.ParseCommentListSortOrThrow(sort, sortDir); // 400 nếu sort/sortDir sai.
        var (unpaged, p, s) = PaginationQuery.ParsePaginationFromQuery(page, pageSize, paginationEnabled); // Parse.
        if (unpaged) // Include toàn tập khớp.
        { // Mở khối.
            var result = await _service.GetAllCommentsEagerLoadingDemoAsync(cancellationToken, filterByPostId, createdAtFrom, createdAtTo, userId, content, sortSpec); // Split query + Include.
            return Ok(new { message = ApiMessages.CommentDemoEagerLoadingAllSuccess, data = result, totalCount = result.Count }); // 200.
        } // Kết thúc unpaged.

        var paged = await _service.GetCommentsEagerLoadingDemoPagedAsync(p, s, cancellationToken, filterByPostId, createdAtFrom, createdAtTo, userId, content, sortSpec); // Trang eager.
        return Ok(new { message = ApiMessages.CommentDemoEagerLoadingListSuccess, data = paged }); // 200.
    } // Kết thúc GetDemoEagerLoadingList.

    // [16] GET /api/comments/demo/explicit-loading
    // Demo explicit: chỉ danh sách — LoadAsync từng bước trên nhiều comment.
    [HttpGet("demo/explicit-loading")] // List.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetDemoExplicitLoadingList( // List.
        [FromQuery(Name = "filterByPostId")] Guid? filterByPostId, // Id Post — tuỳ chọn.
        [FromQuery] Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        [FromQuery] string? content = null, // Tìm trong nội dung (tuỳ chọn).
        [FromQuery] bool paginationEnabled = true, // false = không phân trang.
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        [FromQuery] string? sort = null, // Cột sort.
        [FromQuery] string? sortDir = null, // asc hoặc desc.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var sortSpec = _commentRepository.ParseCommentListSortOrThrow(sort, sortDir); // 400 nếu sort/sortDir sai.
        var (unpaged, p, s) = PaginationQuery.ParsePaginationFromQuery(page, pageSize, paginationEnabled); // Parse.
        if (unpaged) // Mọi dòng khớp + LoadAsync.
        { // Mở khối.
            var result = await _service.GetAllCommentsExplicitLoadingDemoAsync(cancellationToken, filterByPostId, createdAtFrom, createdAtTo, userId, content, sortSpec); // Nhiều câu SQL nhỏ.
            return Ok(new { message = ApiMessages.CommentDemoExplicitLoadingAllSuccess, data = result, totalCount = result.Count }); // 200.
        } // Kết thúc unpaged.

        var paged = await _service.GetCommentsExplicitLoadingDemoPagedAsync(p, s, cancellationToken, filterByPostId, createdAtFrom, createdAtTo, userId, content, sortSpec); // Trang explicit.
        return Ok(new { message = ApiMessages.CommentDemoExplicitLoadingListSuccess, data = paged }); // 200.
    } // Kết thúc GetDemoExplicitLoadingList.

    // [17] GET /api/comments/demo/projection
    // Demo projection: chỉ danh sách — Select DTO trên nhiều comment (không Include graph đầy đủ).
    [HttpGet("demo/projection")] // List.
    [Authorize(Roles = "Admin,User")] // Đọc.
    public async Task<IActionResult> GetDemoProjectionList( // List.
        [FromQuery(Name = "filterByPostId")] Guid? filterByPostId, // Id Post — tuỳ chọn.
        [FromQuery] Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        [FromQuery] string? content = null, // Tìm trong nội dung (tuỳ chọn).
        [FromQuery] bool paginationEnabled = true, // false = không phân trang.
        [FromQuery] string? page = null, // Trang.
        [FromQuery] string? pageSize = null, // Cỡ trang.
        [FromQuery] DateTime? createdAtFrom = null, // Lọc CreatedAt.
        [FromQuery] DateTime? createdAtTo = null, // Lọc CreatedAt.
        [FromQuery] string? sort = null, // Cột sort.
        [FromQuery] string? sortDir = null, // asc hoặc desc.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở action.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo); // 400 nếu khoảng sai.
        var sortSpec = _commentRepository.ParseCommentListSortOrThrow(sort, sortDir); // 400 nếu sort/sortDir sai.
        var (unpaged, p, s) = PaginationQuery.ParsePaginationFromQuery(page, pageSize, paginationEnabled); // Parse.
        if (unpaged) // ToList projection một pipeline.
        { // Mở khối.
            var result = await _service.GetAllCommentsProjectionDemoAsync(cancellationToken, filterByPostId, createdAtFrom, createdAtTo, userId, content, sortSpec); // Không Include graph.
            return Ok(new { message = ApiMessages.CommentDemoProjectionAllSuccess, data = result, totalCount = result.Count }); // 200.
        } // Kết thúc unpaged.

        var paged = await _service.GetCommentsProjectionDemoPagedAsync(p, s, cancellationToken, filterByPostId, createdAtFrom, createdAtTo, userId, content, sortSpec); // Trang projection.
        return Ok(new { message = ApiMessages.CommentDemoProjectionListSuccess, data = paged }); // 200.
    } // Kết thúc GetDemoProjectionList.

}
