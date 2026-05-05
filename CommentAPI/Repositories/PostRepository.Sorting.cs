using CommentAPI; // ApiException.
using CommentAPI.DTOs;
using CommentAPI.Entities;

namespace CommentAPI.Repositories;

// Sort cho list post: whitelist theo PostDto; mặc định CreatedAt giảm (hành vi GetPagedAsync cũ).
public partial class PostRepository
{
    // Giống list cũ: mới nhất trước, Id ổn định khi CreatedAt trùng.
    public static readonly SortByColumn PostListSortDefault = new("CreatedAt", true);

    public SortByColumn ParsePostListSortOrThrow(string? sort, string? sortDir)
    {
        var desc = SortByColumn.ParseDescendingOrThrow(sortDir);
        if (string.IsNullOrWhiteSpace(sort))
            return PostListSortDefault;
        return new SortByColumn(sort.Trim(), desc);
    }

    // Nối OrderBy/ThenBy vào pipeline IQueryable<PostDto> sau Select (đúng cột JSON trả về).
    public IOrderedQueryable<PostDto> ApplyUniversalSorting(IQueryable<PostDto> query, SortByColumn spec)
    {
        var k = spec.Column.Trim().ToLowerInvariant();
        var desc = spec.Descending;
        return k switch
        {
            "id" => desc ? query.OrderByDescending(p => p.Id) : query.OrderBy(p => p.Id),
            "title" => desc
                ? query.OrderByDescending(p => p.Title).ThenBy(p => p.Id)
                : query.OrderBy(p => p.Title).ThenBy(p => p.Id),
            "content" => desc
                ? query.OrderByDescending(p => p.Content).ThenBy(p => p.Id)
                : query.OrderBy(p => p.Content).ThenBy(p => p.Id),
            "createdat" => desc
                ? query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id)
                : query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id),
            "userid" => desc
                ? query.OrderByDescending(p => p.UserId).ThenBy(p => p.Id)
                : query.OrderBy(p => p.UserId).ThenBy(p => p.Id),
            _ => throw new ApiException(400, ApiErrorCodes.PostInvalidSort, ApiMessages.PostInvalidSort),
        };
    }
}
