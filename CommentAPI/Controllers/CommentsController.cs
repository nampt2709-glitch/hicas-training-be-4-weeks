using CommentAPI.DTOs.Comments;
using CommentAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace CommentAPI.Controllers;

[ApiController]
[Route("api/comments")]
public class CommentsController : ControllerBase
{
    private readonly ICommentService _service;

    public CommentsController(ICommentService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await _service.GetAllAsync();
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCommentDto dto)
    {
        var result = await _service.CreateAsync(dto);
        return result is null ? BadRequest("Invalid post, user, or parent comment.") : Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCommentDto dto)
    {
        var updated = await _service.UpdateAsync(id, dto);
        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _service.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("post/{postId:guid}/flat")]
    public async Task<IActionResult> GetFlatByPostId(Guid postId)
    {
        var result = await _service.GetFlatByPostIdAsync(postId);
        return Ok(result);
    }

    [HttpGet("post/{postId:guid}/tree")]
    public async Task<IActionResult> GetTreeByPostId(Guid postId)
    {
        var result = await _service.GetTreeByPostIdAsync(postId);
        return Ok(result);
    }

    [HttpGet("post/{postId:guid}/tree/cte")]
    public async Task<IActionResult> GetTreeByPostIdCte(Guid postId)
    {
        var result = await _service.GetTreeByPostIdCteAsync(postId);
        return Ok(result);
    }
}
