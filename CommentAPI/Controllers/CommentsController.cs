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

    /// <summary>All comments as flat EF rows (no post filter).</summary>
    [HttpGet("all/flat")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAllFlat()
    {
        var result = await _service.GetAllFlatAsync();
        return Ok(new { message = ApiMessages.CommentAllFlatSuccess, data = result });
    }

    /// <summary>Forest of EF trees (one tree per post).</summary>
    [HttpGet("all/tree")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAllTree()
    {
        var result = await _service.GetAllTreeAsync();
        return Ok(new { message = ApiMessages.CommentAllTreeSuccess, data = result });
    }

    /// <summary>All comments: global CTE flat rows with Level.</summary>
    [HttpGet("all/cte")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAllCteFlat()
    {
        var result = await _service.GetAllCteFlatAsync();
        return Ok(new { message = ApiMessages.CommentAllCteFlatSuccess, data = result });
    }

    /// <summary>Forest built from global CTE flat (grouped by post).</summary>
    [HttpGet("all/tree/cte")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetAllCteTree()
    {
        var result = await _service.GetAllCteTreeAsync();
        return Ok(new { message = ApiMessages.CommentAllCteTreeSuccess, data = result });
    }

    /// <summary>Flatten EF forest via preorder DFS (Level + PostId).</summary>
    [HttpGet("flatten")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetFlattenedFromEf()
    {
        var result = await _service.GetFlattenedFromEfAsync();
        return Ok(new { message = ApiMessages.CommentFlattenEfSuccess, data = result });
    }

    /// <summary>Global CTE flat list (SQL recursive flatten).</summary>
    [HttpGet("flatten/cte")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetFlattenedFromCte()
    {
        var result = await _service.GetFlattenedFromCteAsync();
        return Ok(new { message = ApiMessages.CommentFlattenCteSuccess, data = result });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound() : Ok(new { message = ApiMessages.CommentGetSuccess, data = result });
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateCommentDto dto)
    {
        var result = await _service.CreateAsync(dto);
        return result is null
            ? BadRequest(new { message = ApiMessages.CommentCreateInvalidRefs })
            : Ok(new { message = ApiMessages.CommentCreateSuccess, data = result });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCommentDto dto)
    {
        var updated = await _service.UpdateAsync(id, dto);
        return updated ? Ok(new { message = ApiMessages.CommentUpdateSuccess }) : NotFound();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _service.DeleteAsync(id);
        return deleted ? Ok(new { message = ApiMessages.CommentDeleteSuccess }) : NotFound();
    }

    /// <summary>Danh sách comment phẳng theo post (truy vấn EF).</summary>
    [HttpGet("post/{postId:guid}/flat")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetFlatByPostId(Guid postId)
    {
        var result = await _service.GetFlatByPostIdAsync(postId);
        return result is null
            ? NotFound(new { code = ApiErrorCodes.PostNotFound, message = ApiMessages.PostNotFoundMessage })
            : Ok(new { message = ApiMessages.CommentFlatByPostSuccess, data = result });
    }

    /// <summary>Danh sách comment phẳng kèm Level (CTE đệ quy trên SQL).</summary>
    [HttpGet("post/{postId:guid}/cte")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetCteFlatByPostId(Guid postId)
    {
        var result = await _service.GetCteFlatByPostIdAsync(postId);
        return result is null
            ? NotFound(new { code = ApiErrorCodes.PostNotFound, message = ApiMessages.PostNotFoundMessage })
            : Ok(new { message = ApiMessages.CommentCteFlatByPostSuccess, data = result });
    }

    /// <summary>Cây comment dựng từ danh sách phẳng (EF).</summary>
    [HttpGet("post/{postId:guid}/tree")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetTreeByPostId(Guid postId)
    {
        var result = await _service.GetTreeByPostIdAsync(postId);
        return result is null
            ? NotFound(new { code = ApiErrorCodes.PostNotFound, message = ApiMessages.PostNotFoundMessage })
            : Ok(new { message = ApiMessages.CommentTreeByPostSuccess, data = result });
    }

    /// <summary>Cây comment dựng từ hàng phẳng do CTE trả về.</summary>
    [HttpGet("post/{postId:guid}/tree/cte")]
    [Authorize(Roles = "Admin,User")]
    public async Task<IActionResult> GetCteTreeByPostId(Guid postId)
    {
        var result = await _service.GetCteTreeByPostIdAsync(postId);
        return result is null
            ? NotFound(new { code = ApiErrorCodes.PostNotFound, message = ApiMessages.PostNotFoundMessage })
            : Ok(new { message = ApiMessages.CommentCteTreeByPostSuccess, data = result });
    }
}
