using CommentAPI; // ApiException.
using CommentAPI.DTOs; // UserDto projection sort keys.
using CommentAPI.Entities; // User.
using Microsoft.EntityFrameworkCore; // AsNoTracking trên DbSet Identity.

namespace CommentAPI.Repositories;

// Sort user list: whitelist theo cột UserDto (Roles = Min tên role theo subquery).
public partial class UserRepository
{
    // Giữ hành vi list cũ: CreatedAt tăng dần, ThenBy Id.
    public static readonly SortByColumn UserListSortDefault = new("CreatedAt", false);

    public SortByColumn ParseUserListSortOrThrow(string? sort, string? sortDir)
    {
        var desc = SortByColumn.ParseDescendingOrThrow(sortDir);
        if (string.IsNullOrWhiteSpace(sort))
            return UserListSortDefault;
        return new SortByColumn(sort.Trim(), desc);
    }

    // Áp sau filter, trước Skip/Take + projection UserPageRow.
    public IOrderedQueryable<User> ApplyUniversalSorting(IQueryable<User> query, SortByColumn spec)
    {
        var k = spec.Column.Trim().ToLowerInvariant();
        var desc = spec.Descending;

        if (k == "roles")
        {
            // Min tên role (alphabet) mỗi user — subquery tương quan theo u.Id.
            return desc
                ? query.OrderByDescending(u => Context.UserRoles.AsNoTracking()
                        .Where(ur => ur.UserId == u.Id)
                        .Join(
                            Context.Roles.AsNoTracking(),
                            ur => ur.RoleId,
                            r => r.Id,
                            (ur, r) => r.Name)
                        .Min())
                    .ThenBy(u => u.Id)
                : query.OrderBy(u => Context.UserRoles.AsNoTracking()
                        .Where(ur => ur.UserId == u.Id)
                        .Join(
                            Context.Roles.AsNoTracking(),
                            ur => ur.RoleId,
                            r => r.Id,
                            (ur, r) => r.Name)
                        .Min())
                    .ThenBy(u => u.Id);
        }

        return k switch
        {
            "id" => desc ? query.OrderByDescending(u => u.Id) : query.OrderBy(u => u.Id),
            "name" => desc
                ? query.OrderByDescending(u => u.Name).ThenBy(u => u.Id)
                : query.OrderBy(u => u.Name).ThenBy(u => u.Id),
            "username" => desc
                ? query.OrderByDescending(u => u.UserName).ThenBy(u => u.Id)
                : query.OrderBy(u => u.UserName).ThenBy(u => u.Id),
            "email" => desc
                ? query.OrderByDescending(u => u.Email).ThenBy(u => u.Id)
                : query.OrderBy(u => u.Email).ThenBy(u => u.Id),
            "createdat" => desc
                ? query.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id)
                : query.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id),
            _ => throw new ApiException(400, ApiErrorCodes.UserInvalidSort, ApiMessages.UserInvalidSort),
        };
    }
}
