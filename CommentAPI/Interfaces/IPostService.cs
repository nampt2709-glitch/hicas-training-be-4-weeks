using CommentAPI.DTOs;

namespace CommentAPI.Interfaces;

public interface IPostService
{
    Task<PagedResult<PostDto>> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Tìm post theo tiêu đề (chuỗi chứa), có phân trang.</summary>
    Task<PagedResult<PostDto>> SearchByTitlePagedAsync(
        string? title,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PostDto> GetByIdAsync(Guid id);
    Task<PostDto> CreateAsync(CreatePostDto dto);
    Task UpdateAsync(Guid id, UpdatePostDto dto);
    Task DeleteAsync(Guid id);
}
