using CommentAPI.DTOs;

namespace CommentAPI.Interfaces;

public interface ICommentService
{
    Task<List<CommentDto>> GetAllAsync();
    Task<CommentDto?> GetByIdAsync(Guid id);
    Task<CommentDto?> CreateAsync(CreateCommentDto dto);
    Task<bool> UpdateAsync(Guid id, UpdateCommentDto dto);
    Task<bool> DeleteAsync(Guid id);

    /// <summary>Flat list (EF). Null if the post does not exist.</summary>
    Task<List<CommentDto>?> GetFlatByPostIdAsync(Guid postId);

    /// <summary>Tree from EF flat rows. Null if the post does not exist.</summary>
    Task<List<CommentTreeDto>?> GetTreeByPostIdAsync(Guid postId);

    /// <summary>Flat rows with Level (CTE). Null if the post does not exist.</summary>
    Task<List<CommentFlatDto>?> GetCteFlatByPostIdAsync(Guid postId);

    /// <summary>Tree built from CTE flat rows. Null if the post does not exist.</summary>
    Task<List<CommentTreeDto>?> GetCteTreeByPostIdAsync(Guid postId);

    /// <summary>All comments as EF flat rows ordered by PostId, CreatedAt, Id.</summary>
    Task<List<CommentDto>> GetAllFlatAsync();

    /// <summary>Forest: one tree per post, roots concatenated.</summary>
    Task<List<CommentTreeDto>> GetAllTreeAsync();

    /// <summary>Global CTE flat rows with Level.</summary>
    Task<List<CommentFlatDto>> GetAllCteFlatAsync();

    /// <summary>Forest built from global CTE flat (grouped by PostId).</summary>
    Task<List<CommentTreeDto>> GetAllCteTreeAsync();

    /// <summary>Preorder DFS flatten after EF tree build.</summary>
    Task<List<CommentFlatDto>> GetFlattenedFromEfAsync();

    /// <summary>Global recursive CTE flat list (SQL-side flatten).</summary>
    Task<List<CommentFlatDto>> GetFlattenedFromCteAsync();
}
