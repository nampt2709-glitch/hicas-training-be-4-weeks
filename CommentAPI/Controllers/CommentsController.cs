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
    public async Task<IActionResult> GetAll()
    {
        var result = await _service.GetAllAsync();
        return Ok(new { message = ApiMessages.CommentListSuccess, data = result });
    }

    [HttpGet("all/flat")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAllFlat()
    {
        var result = await _service.GetAllFlatAsync();
        return Ok(new { message = ApiMessages.CommentAllFlatSuccess, data = result });
    }

    [HttpGet("all/tree")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAllTree()
    {
        var result = await _service.GetAllTreeAsync();
        return Ok(new { message = ApiMessages.CommentAllTreeSuccess, data = result });
    }

    [HttpGet("all/cte")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAllCteFlat()
    {
        var result = await _service.GetAllCteFlatAsync();
        return Ok(new { message = ApiMessages.CommentAllCteFlatSuccess, data = result });
    }

    [HttpGet("all/tree/cte")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAllCteTree()
    {
        var result = await _service.GetAllCteTreeAsync();
        return Ok(new { message = ApiMessages.CommentAllCteTreeSuccess, data = result });
    }

    [HttpGet("flatten")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetFlattenedFromEf()
    {
        var result = await _service.GetFlattenedFromEfAsync();
        return Ok(new { message = ApiMessages.CommentFlattenEfSuccess, data = result });
    }

    [HttpGet("flatten/cte")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetFlattenedFromCte()
    {
        var result = await _service.GetFlattenedFromCteAsync();
        return Ok(new { message = ApiMessages.CommentFlattenCteSuccess, data = result });
    }

    [HttpGet("tree/flatten")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetTreeFlattenAll()
    {
        var result = await _service.GetFlattenedForestAsync();
        return Ok(new { message = ApiMessages.CommentFlattenForestSuccess, data = result });
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

    [HttpGet("post/{postId:guid}/flat")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetFlatByPostId(Guid postId)
    {
        var result = await _service.GetFlatByPostIdAsync(postId);
        return Ok(new { message = ApiMessages.CommentFlatByPostSuccess, data = result });
    }

    [HttpGet("post/{postId:guid}/cte")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetCteFlatByPostId(Guid postId)
    {
        var result = await _service.GetCteFlatByPostIdAsync(postId);
        return Ok(new { message = ApiMessages.CommentCteFlatByPostSuccess, data = result });
    }

    [HttpGet("post/{postId:guid}/tree")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetTreeByPostId(Guid postId)
    {
        var result = await _service.GetTreeByPostIdAsync(postId);
        return Ok(new { message = ApiMessages.CommentTreeByPostSuccess, data = result });
    }

    [HttpGet("post/{postId:guid}/tree/cte")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetCteTreeByPostId(Guid postId)
    {
        var result = await _service.GetCteTreeByPostIdAsync(postId);
        return Ok(new { message = ApiMessages.CommentCteTreeByPostSuccess, data = result });
    }

    [HttpGet("post/{postId:guid}/tree/flatten")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetTreeFlattenByPostId(Guid postId)
    {
        var result = await _service.GetFlattenedTreeByPostIdAsync(postId);
        return Ok(new { message = ApiMessages.CommentFlattenTreeByPostSuccess, data = result });
    }
}
