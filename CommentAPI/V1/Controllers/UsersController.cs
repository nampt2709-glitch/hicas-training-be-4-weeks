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
// V1 — chỉ CRUD user cơ bản. PUT /admin/users/... → API v2.0.
// =============================================================================

[ApiController]
[ApiVersion("1.0")]

[Authorize]

[Route("api/v{version:apiVersion}/users")]

public class UsersController : ControllerBase
{

    private readonly IUserService _service;

    private readonly IUserRepository _userRepository;

    public UsersController(IUserService service, IUserRepository userRepository)
    {

        _service = service;

        _userRepository = userRepository;

    }

    [HttpGet]

    [Authorize(Roles = "Admin,User")]

    public async Task<IActionResult> GetAll(

        [FromQuery] string? page,

        [FromQuery] string? pageSize,

        [FromQuery] string? name = null,

        [FromQuery] string? userName = null,

        [FromQuery] string? email = null,

        [FromQuery] DateTime? createdAtFrom = null,

        [FromQuery] DateTime? createdAtTo = null,

        [FromQuery] string? sort = null,

        [FromQuery] string? sortDir = null,

        CancellationToken cancellationToken = default)
    {

        CreatedAtRangeQuery.ValidateOrThrow(createdAtFrom, createdAtTo);

        var (p, s) = PaginationQuery.ParseFromQuery(page, pageSize);

        var sortSpec = _userRepository.ParseUserListSortOrThrow(sort, sortDir);

        var result = await _service.GetPagedAsync(p, s, cancellationToken, createdAtFrom, createdAtTo, name, userName, email, sortSpec);

        return Ok(new { message = ApiMessages.UserListSuccess, data = result });

    }

    [HttpGet("{id:guid}")]

    [Authorize(Roles = "Admin,User")]

    public async Task<IActionResult> GetById(Guid id)
    {

        var result = await _service.GetByIdAsync(id);

        return Ok(new { message = ApiMessages.UserGetSuccess, data = result });

    }

    [HttpPost]

    [Authorize(Roles = "Admin")]

    public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
    {

        var result = await _service.CreateAsync(dto);

        return CreatedAtAction(

            nameof(GetById),

            ApiVersionRouteValues.WithVersion(this, new { id = result.Id }),

            new { message = ApiMessages.UserCreateSuccess, data = result });

    }

    [HttpPut("{id:guid}")]

    [Authorize(Roles = "Admin,User")]

    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDto dto)
    {

        if (User.IsInRole("Admin"))
        {

            return StatusCode(

                StatusCodes.Status403Forbidden,

                new

                {

                    code = ApiErrorCodes.UserUseAdminUpdateEndpoint,

                    message = ApiMessages.UserUseAdminUpdateEndpoint

                });

        }

        var userId = HttpContextUserId.GetRequiredUserId(User);

        await _service.UpdateAsSelfAsync(id, dto, userId);

        return Ok(new { message = ApiMessages.UserUpdateSuccess });

    }

    [HttpDelete("{id:guid}")]

    [Authorize(Roles = "Admin")]

    public async Task<IActionResult> Delete(Guid id)
    {

        await _service.DeleteAsync(id);

        return Ok(new { message = ApiMessages.UserDeleteSuccess });

    }

}
