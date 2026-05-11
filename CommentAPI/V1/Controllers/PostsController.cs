using Asp.Versioning;
using CommentAPI;
using CommentAPI.Controllers;
using CommentAPI.Versioning;

using CommentAPI.DTOs;
using CommentAPI.Validators;

using CommentAPI.Interfaces;

using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Http;

using Microsoft.AspNetCore.Mvc;

namespace CommentAPI.V1.Controllers;

// =============================================================================
// V1 — chỉ CRUD post cơ bản. Sub-resource comment theo post, PUT admin → API v2.0.
// =============================================================================

[ApiController]
[ApiVersion("1.0")]

[Authorize]

[Route("api/v{version:apiVersion}/posts")]

public class PostsController : ControllerBase
{

    private readonly IPostService _service;

    private readonly IPostRepository _postRepository;

    public PostsController(
        IPostService service,
        IPostRepository postRepository)
    {

        _service = service;

        _postRepository = postRepository;

    }

    [HttpGet]

    [Authorize(Roles = "Admin,User")]

    public async Task<IActionResult> GetAll(

        [FromQuery] string? page,

        [FromQuery] string? pageSize,

        [FromQuery] string? title = null,

        [FromQuery] string? content = null,

        [FromQuery] DateTime? createdAtFrom = null,

        [FromQuery] DateTime? createdAtTo = null,

        [FromQuery] string? sort = null,

        [FromQuery] string? sortDir = null,

        CancellationToken cancellationToken = default)

    {

        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);

        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);

        var sortSpec = _postRepository.ParsePostListSortOrThrow(sort, sortDir);

        var result = await _service.GetPagedAsync(p, s, cancellationToken, createdAtFrom, createdAtTo, title, content, sortSpec);

        return Ok(new { message = ApiMessages.PostListSuccess, data = result });

    }

    [HttpGet("{id:guid}")]

    [Authorize(Roles = "Admin,User")]

    public async Task<IActionResult> GetById(Guid id)
    {

        var result = await _service.GetByIdAsync(id);

        return Ok(new { message = ApiMessages.PostGetSuccess, data = result });

    }

    [HttpPost]

    [Authorize(Roles = "Admin,User")]

    public async Task<IActionResult> Create([FromBody] CreatePostDto dto)
    {

        var result = await _service.CreateAsync(dto);

        return CreatedAtAction(

            nameof(GetById),

            ApiVersionRouteValues.WithVersion(this, new { id = result.Id }),

            new { message = ApiMessages.PostCreateSuccess, data = result });

    }

    [HttpPut("{id:guid}")]

    [Authorize(Roles = "Admin,User")]

    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePostDto dto)
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

        return Ok(new { message = ApiMessages.PostUpdateSuccess });

    }

    [HttpDelete("{id:guid}")]

    [Authorize(Roles = "Admin")]

    public async Task<IActionResult> Delete(Guid id)
    {

        await _service.DeleteAsync(id);

        return Ok(new { message = ApiMessages.PostDeleteSuccess });

    }

}
