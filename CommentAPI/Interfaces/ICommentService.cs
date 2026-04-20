using CommentAPI.DTOs;

namespace CommentAPI.Interfaces;

public interface ICommentService
{
    Task<List<CommentDto>> GetAllAsync();
    Task<CommentDto> GetByIdAsync(Guid id);
    Task<CommentDto> CreateAsync(CreateCommentDto dto);
    Task UpdateAsync(Guid id, UpdateCommentDto dto);
    Task DeleteAsync(Guid id);

    /// <summary>Danh sách phẳng theo post; ném nếu post không tồn tại.</summary>
    Task<List<CommentDto>> GetFlatByPostIdAsync(Guid postId);

    /// <summary>Cây EF theo post; ném nếu post không tồn tại.</summary>
    Task<List<CommentTreeDto>> GetTreeByPostIdAsync(Guid postId);

    /// <summary>Hàng phẳng CTE theo post; ném nếu post không tồn tại.</summary>
    Task<List<CommentFlatDto>> GetCteFlatByPostIdAsync(Guid postId);

    /// <summary>Cây từ CTE theo post; ném nếu post không tồn tại.</summary>
    Task<List<CommentTreeDto>> GetCteTreeByPostIdAsync(Guid postId);

    Task<List<CommentDto>> GetAllFlatAsync();
    Task<List<CommentTreeDto>> GetAllTreeAsync();
    Task<List<CommentFlatDto>> GetAllCteFlatAsync();
    Task<List<CommentTreeDto>> GetAllCteTreeAsync();
    Task<List<CommentFlatDto>> GetFlattenedFromEfAsync();
    Task<List<CommentFlatDto>> GetFlattenedForestAsync();

    /// <summary>Một post: cây EF rồi DFS; ném nếu post không tồn tại.</summary>
    Task<List<CommentFlatDto>> GetFlattenedTreeByPostIdAsync(Guid postId);

    Task<List<CommentFlatDto>> GetFlattenedFromCteAsync();
}
