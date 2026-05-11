using ApartmentAPI.DTOs; // ApiMessages, PaginationQuery.
using ApartmentAPI.Services; // IFeedbackService — phản hồi và cây thread.
using ApartmentAPI.Validators; // CreatedAtRangeQuery.
using ApartmentAPI.V1.DTOs; // DTO feedback phiên bản 1.
using ApartmentAPI.Versioning; // ApiVersionRouteValues.WithVersion.
using Asp.Versioning; // [ApiVersion].
using ApartmentAPI.Authorization; // ApiAuthorization — chuỗi role thống nhất; gộp với [Authorize] trên action admin.
using Microsoft.AspNetCore.Authorization; // [Authorize(Roles = ...)] — lớp: Admin/User; PUT .../admin: chỉ Admin.
using Microsoft.AspNetCore.Mvc; // ControllerBase, IActionResult.

namespace ApartmentAPI.V2.Controllers;

// V2 — đầy đủ route so với V1: by-user, roots, PUT .../admin; có cây phản hồi. Admin hoặc User; PUT admin chỉ Admin.
[ApiController]
[ApiVersion("2.0")]
[Authorize(Roles = ApiAuthorization.AdminOrUser)]
[Route("api/v{version:apiVersion}/feedbacks")]
public class FeedbacksController : ControllerBase
{
    private readonly IFeedbackService _service;

    public FeedbacksController(IFeedbackService service) => _service = service;

    private string? DeletedBy() => User.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] string? contentContains = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetPagedAsync(p, s, createdAtFrom, createdAtTo, null, false, contentContains, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.FeedbackListSuccess, data });
    }

    // CTE đệ quy — danh sách phẳng có Level (SqlQueryRaw); tham số giống CommentAPI GET /comments/cte (không postId).
    [HttpGet("cte")]
    public async Task<IActionResult> GetCteFlat(
        [FromQuery] Guid? userId = null,
        [FromQuery] string? contentContains = null,
        [FromQuery] string? page = null,
        [FromQuery] string? pageSize = null,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    { // Mở khối GetCteFlat — một SQL CTE + phân trang dòng phẳng.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetCteFlatRoutePagedAsync(p, s, createdAtFrom, createdAtTo, userId, contentContains, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.FeedbackCteFlatSuccess, data });
    } // Kết thúc GetCteFlat.

    // Cây lồng từ hàng CTE — phân trang theo số thread gốc.
    [HttpGet("tree/cte")]
    public async Task<IActionResult> GetTreeCte(
        [FromQuery] Guid? userId = null,
        [FromQuery] string? contentContains = null,
        [FromQuery] string? page = null,
        [FromQuery] string? pageSize = null,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    { // Mở khối GetTreeCte — BuildFeedbackTreeCte rồi cắt trang theo gốc.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetTreeCteRoutePagedAsync(p, s, createdAtFrom, createdAtTo, userId, contentContains, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.FeedbackCteTreeSuccess, data });
    } // Kết thúc GetTreeCte.

    // Preorder flatten sau tree/cte — mỗi trang gốc mở rộng thành nhiều dòng phẳng.
    [HttpGet("tree/cte/flatten")]
    public async Task<IActionResult> GetTreeCteFlatten(
        [FromQuery] Guid? userId = null,
        [FromQuery] string? contentContains = null,
        [FromQuery] string? page = null,
        [FromQuery] string? pageSize = null,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    { // Mở khối GetTreeCteFlatten — flatten giữ Level từ cây CTE.
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetTreeCteFlattenRoutePagedAsync(p, s, createdAtFrom, createdAtTo, userId, contentContains, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.FeedbackCteFlattenSuccess, data });
    } // Kết thúc GetTreeCteFlatten.

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var data = await _service.GetByIdAsync(id, ct);
        return Ok(new { message = ApiMessages.FeedbackGetSuccess, data });
    }

    [HttpGet("by-user/{userId:guid}")]
    public async Task<IActionResult> GetByUser(
        Guid userId,
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] string? contentContains = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetPagedAsync(p, s, createdAtFrom, createdAtTo, userId, false, contentContains, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.FeedbackListSuccess, data });
    }

    [HttpGet("roots")]
    public async Task<IActionResult> GetRoots(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] string? contentContains = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _service.GetPagedAsync(p, s, createdAtFrom, createdAtTo, null, true, contentContains, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.FeedbackListSuccess, data });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFeedbackDto dto, CancellationToken ct)
    {
        var data = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), ApiVersionRouteValues.WithVersion(this, new { id = data.Id }), new { message = ApiMessages.Ok, data });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFeedbackDto dto, CancellationToken ct)
    {
        await _service.UpdateAsync(id, dto, ct);
        return Ok(new { message = ApiMessages.Ok });
    }

    [HttpPut("{id:guid}/admin")]
    [Authorize(Roles = ApiAuthorization.AdminOnly)]
    public async Task<IActionResult> UpdateAsAdmin(Guid id, [FromBody] AdminUpdateFeedbackDto dto, CancellationToken ct)
    {
        await _service.UpdateAsAdminAsync(id, dto, ct);
        return Ok(new { message = ApiMessages.Ok });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        await _service.SoftDeleteAsync(id, DeletedBy(), ct);
        return Ok(new { message = ApiMessages.Ok });
    }
}
