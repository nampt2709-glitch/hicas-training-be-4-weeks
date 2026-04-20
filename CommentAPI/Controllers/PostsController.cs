using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CommentAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/posts")]
public class PostsController : ControllerBase
{
    private readonly IPostService _service;

    public PostsController(IPostService service)
    {
        _service = service;
    }

    [HttpGet]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAll()
    {
        var result = await _service.GetAllAsync();
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
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreatePostDto dto)
    {
        var result = await _service.CreateAsync(dto);
        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Id },
            new { message = ApiMessages.PostCreateSuccess, data = result });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePostDto dto)
    {
        await _service.UpdateAsync(id, dto);
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
