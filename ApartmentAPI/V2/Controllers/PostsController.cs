using ApartmentAPI.DTOs; // ApiMessages, PaginationQuery.
using ApartmentAPI.Services; // IPostService, IAttachmentService.
using ApartmentAPI.Validators; // CreatedAtRangeQuery.
using ApartmentAPI.V1.DTOs; // Post DTO; PostAttachmentUploadModel cho multipart đính kèm.
using ApartmentAPI.Versioning; // ApiVersionRouteValues.WithVersion.
using Asp.Versioning; // [ApiVersion].
using ApartmentAPI.Authorization; // ApiAuthorization.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApartmentAPI.V2.Controllers;

// V2 — CRUD Post + POST .../posts/{id}/attachments (tiện REST). Thêm route tương đương: .../attachments/posts/{postId}/files.
[ApiController]
[ApiVersion("2.0")]
[Authorize(Roles = ApiAuthorization.AdminOrUser)]
[Route("api/v{version:apiVersion}/posts")]
public class PostsController : ControllerBase
{
    private readonly IPostService _posts;
    private readonly IAttachmentService _attachments;

    public PostsController(IPostService posts, IAttachmentService attachments)
    {
        _posts = posts;
        _attachments = attachments;
    }

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

    // POST .../posts/{id}/attachments — giống yêu cầu REST gốc; UserId file = tác giả bài (service).
    [HttpPost("{id:guid}/attachments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(35 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 35 * 1024 * 1024)]
    public async Task<IActionResult> AddAttachment(Guid id, [FromForm] PostAttachmentUploadModel model, CancellationToken ct)
    {
        var data = await _attachments.CreateForPostAsync(id, model.File, ct);
        return CreatedAtAction(
            nameof(AttachmentsController.GetById),
            "Attachments",
            ApiVersionRouteValues.WithVersion(this, new { id = data.Id }),
            new { message = ApiMessages.Ok, data });
    }
}
