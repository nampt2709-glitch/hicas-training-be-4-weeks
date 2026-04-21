using CommentAPI.DTOs;
using CommentAPI.Entities;

namespace CommentAPI.Interfaces;

public interface IPostRepository
{
    Task<List<Post>> GetAllAsync();

    /// <summary>Phân trang — Select thẳng <see cref="PostDto"/> (không materialize <see cref="Post"/>).</summary>
    Task<(List<PostDto> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Tìm post có <see cref="Post.Title"/> chứa chuỗi (phân trang).</summary>
    Task<(List<PostDto> Items, long TotalCount)> SearchByTitlePagedAsync(
        string titleContains,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Đọc GET theo id — projection, AsNoTracking.</summary>
    Task<PostDto?> GetByIdForReadAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Post?> GetByIdAsync(Guid id);
    Task AddAsync(Post post);
    void Update(Post post);
    void Remove(Post post);
    Task<bool> ExistsAsync(Guid id);
    Task SaveChangesAsync();
}
