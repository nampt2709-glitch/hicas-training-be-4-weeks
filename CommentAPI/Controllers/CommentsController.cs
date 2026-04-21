using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CommentAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/comments")]
public class CommentsController : ControllerBase
{
    private readonly ICommentService _service;

    public CommentsController(ICommentService service)
    {
        _service = service;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetAllPagedAsync(p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentListSuccess, data = result });
    }

    [HttpGet("search/id/{id:guid}")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> SearchById(Guid id)
    {
        var result = await _service.GetByIdAsync(id);
        return Ok(new { message = ApiMessages.CommentGetSuccess, data = result });
    }

    [HttpGet("search/by-content")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> SearchByContent(
        [FromQuery] string? content,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.SearchByContentPagedAsync(content, p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentSearchByContentSuccess, data = result });
    }

    [HttpGet("all/flat")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAllFlat(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetAllFlatPagedAsync(p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentAllFlatSuccess, data = result });
    }

    [HttpGet("all/tree/flat")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAllTreeFlat(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetAllTreePagedAsync(p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentAllTreeSuccess, data = result });
    }

    [HttpGet("all/cte")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAllCteFlat(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetAllCteFlatPagedAsync(p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentAllCteFlatSuccess, data = result });
    }

    [HttpGet("all/tree/cte")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAllCteTree(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetAllCteTreePagedAsync(p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentAllCteTreeSuccess, data = result });
    }

    /// <summary>Toàn cục: trang gốc EF → cây → preorder phẳng (không CTE).</summary>
    [HttpGet("all/tree/flat/flatten")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAllTreeFlatFlattened(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetFlattenedForestPagedAsync(p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentFlattenForestSuccess, data = result });
    }

    /// <summary>Toàn cục: CTE mọi post → cây → preorder; phân trang theo dòng phẳng.</summary>
    [HttpGet("tree/cte/flatten")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAllCteTreeFlattened(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetFlattenedFromCtePagedAsync(p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentFlattenCteSuccess, data = result });
    }

    /// <summary>Demo phân trang — lazy: mỗi bản ghi tracked, đọc Post/User/Children kích hoạt thêm truy vấn.</summary>
    [HttpGet("demo/lazy-loading")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetDemoLazyLoadingList(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetCommentsLazyLoadingDemoPagedAsync(p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentDemoLazyLoadingListSuccess, data = result });
    }

    /// <summary>Demo phân trang — eager: Include Post, User, Parent, Children (AsSplitQuery).</summary>
    [HttpGet("demo/eager-loading")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetDemoEagerLoadingList(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetCommentsEagerLoadingDemoPagedAsync(p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentDemoEagerLoadingListSuccess, data = result });
    }

    /// <summary>Demo phân trang — explicit: sau Skip/Take, LoadAsync từng navigation cho mỗi comment.</summary>
    [HttpGet("demo/explicit-loading")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetDemoExplicitLoadingList(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetCommentsExplicitLoadingDemoPagedAsync(p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentDemoExplicitLoadingListSuccess, data = result });
    }

    /// <summary>Demo phân trang — projection: Select DTO trên server (join + COUNT con), không Include.</summary>
    [HttpGet("demo/projection")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetDemoProjectionList(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetCommentsProjectionDemoPagedAsync(p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentDemoProjectionListSuccess, data = result });
    }

    /// <summary>Demo một comment — lazy loading (proxies).</summary>
    [HttpGet("demo/lazy-loading/{id:guid}")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetDemoLazyLoading(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetCommentLazyLoadingDemoAsync(id, cancellationToken);
        return Ok(new { message = ApiMessages.CommentDemoLazyLoadingSuccess, data = result });
    }

    /// <summary>Demo một comment — eager (Include / AsSplitQuery).</summary>
    [HttpGet("demo/eager-loading/{id:guid}")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetDemoEagerLoading(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetCommentEagerLoadingDemoAsync(id, cancellationToken);
        return Ok(new { message = ApiMessages.CommentDemoEagerLoadingSuccess, data = result });
    }

    /// <summary>Demo một comment — explicit (Entry LoadAsync).</summary>
    [HttpGet("demo/explicit-loading/{id:guid}")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetDemoExplicitLoading(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetCommentExplicitLoadingDemoAsync(id, cancellationToken);
        return Ok(new { message = ApiMessages.CommentDemoExplicitLoadingSuccess, data = result });
    }

    /// <summary>Demo một comment — projection (Select SQL, không nạp navigation trên client).</summary>
    [HttpGet("demo/projection/{id:guid}")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetDemoProjection(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetCommentProjectionDemoAsync(id, cancellationToken);
        return Ok(new { message = ApiMessages.CommentDemoProjectionSuccess, data = result });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _service.GetByIdAsync(id);
        return Ok(new { message = ApiMessages.CommentGetSuccess, data = result });
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateCommentDto dto)
    {
        var result = await _service.CreateAsync(dto);
        return Ok(new { message = ApiMessages.CommentCreateSuccess, data = result });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCommentDto dto)
    {
        await _service.UpdateAsync(id, dto);
        return Ok(new { message = ApiMessages.CommentUpdateSuccess });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.DeleteAsync(id);
        return Ok(new { message = ApiMessages.CommentDeleteSuccess });
    }

    /// <summary>Tìm một comment theo id trong phạm vi một post.</summary>
    [HttpGet("post/{postId:guid}/search/id/{commentId:guid}")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> SearchByIdInPost(Guid postId, Guid commentId, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetByIdInPostAsync(postId, commentId, cancellationToken);
        return Ok(new { message = ApiMessages.CommentSearchByIdInPostSuccess, data = result });
    }

    /// <summary>Tìm theo nội dung chỉ trong một post (phân trang).</summary>
    [HttpGet("post/{postId:guid}/search/by-content")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> SearchByContentInPost(
        Guid postId,
        [FromQuery] string? content,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.SearchByContentInPostPagedAsync(postId, content, p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentSearchByContentInPostSuccess, data = result });
    }

    [HttpGet("post/{postId:guid}/flat")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetFlatByPostId(
        Guid postId,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetFlatByPostIdPagedAsync(postId, p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentFlatByPostSuccess, data = result });
    }

    [HttpGet("post/{postId:guid}/cte")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetCteFlatByPostId(
        Guid postId,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetCteFlatByPostIdPagedAsync(postId, p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentCteFlatByPostSuccess, data = result });
    }

    [HttpGet("post/{postId:guid}/tree/flat")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetTreeByPostId(
        Guid postId,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetTreeByPostIdPagedAsync(postId, p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentTreeByPostSuccess, data = result });
    }

    [HttpGet("post/{postId:guid}/tree/cte")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetCteTreeByPostId(
        Guid postId,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetCteTreeByPostIdPagedAsync(postId, p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentCteTreeByPostSuccess, data = result });
    }

    /// <summary>CTE một post → cây → danh sách phẳng preorder (khác <c>.../cte</c> là thứ tự/Level theo DFS).</summary>
    [HttpGet("post/{postId:guid}/tree/cte/flatten")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetFlattenedCteTreeByPostId(
        Guid postId,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetFlattenedCteTreeByPostIdPagedAsync(postId, p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentFlattenCteTreeByPostSuccess, data = result });
    }

    [HttpGet("post/{postId:guid}/tree/flat/flatten")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetTreeFlattenByPostId(
        Guid postId,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var result = await _service.GetFlattenedTreeByPostIdPagedAsync(postId, p, s, cancellationToken);
        return Ok(new { message = ApiMessages.CommentFlattenTreeByPostSuccess, data = result });
    }
}
