using ApartmentAPI.DTOs; // ApiMessages, PaginationQuery.
using ApartmentAPI.Services; // IPostService — V1 chỉ CRUD Post.
using ApartmentAPI.Validators; // CreatedAtRangeQuery.
using ApartmentAPI.V1.DTOs; // Post DTO.
using ApartmentAPI.Versioning; // ApiVersionRouteValues.WithVersion.
using Asp.Versioning; // [ApiVersion].
using ApartmentAPI.Authorization; // ApiAuthorization.
using Microsoft.AspNetCore.Authorization; // [Authorize(Roles = ...)].
using Microsoft.AspNetCore.Mvc; // ControllerBase.

namespace ApartmentAPI.V1.Controllers;

// V1 — chỉ CRUD cơ bản Post (GET list/detail, POST, PUT, DELETE). Đính kèm bài viết → V2 (attachments/posts/... hoặc posts/... tùy V2).
[ApiController]
[ApiVersion("1.0")]
[Authorize(Roles = ApiAuthorization.AdminOrUser)]
[Route("api/v{version:apiVersion}/posts")]
public class PostsController : ControllerBase
{
    private readonly IPostService _posts;

    public PostsController(IPostService posts) => _posts = posts;

    private string? DeletedBy() => User.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? page,
        [FromQuery] string? pageSize,
        [FromQuery] DateTime? createdAtFrom = null,
        [FromQuery] DateTime? createdAtTo = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] Guid? apartmentId = null,
        [FromQuery] bool? isPublished = null,
        [FromQuery] string? titleContains = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? sortDir = null,
        CancellationToken ct = default)
    {
        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);
        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);
        var data = await _posts.GetPagedAsync(
            p, s, createdAtFrom, createdAtTo, userId, apartmentId, isPublished, titleContains, sort, sortDir, ct);
        return Ok(new { message = ApiMessages.PostListSuccess, data });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var data = await _posts.GetByIdAsync(id, ct);
        return Ok(new { message = ApiMessages.PostGetSuccess, data });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePostDto dto, CancellationToken ct)
    {
        var data = await _posts.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), ApiVersionRouteValues.WithVersion(this, new { id = data.Id }), new { message = ApiMessages.Ok, data });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePostDto dto, CancellationToken ct)
    {
        await _posts.UpdateAsync(id, dto, ct);
        return Ok(new { message = ApiMessages.Ok });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        await _posts.SoftDeleteAsync(id, DeletedBy(), ct);
        return Ok(new { message = ApiMessages.Ok });
    }
}
