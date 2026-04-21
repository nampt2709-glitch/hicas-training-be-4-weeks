using CommentAPI.Entities;
using CommentAPI.Interfaces;
using CommentAPI.Data;
using CommentAPI.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<User>> GetAllAsync()
    {
        return await _context.Users
            .AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<(List<UserPageRow> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Chuẩn hóa Skip/Take; Select chỉ cột cần cho DTO (không đọc PasswordHash, token…).
        var (p, s) = PaginationQuery.Normalize(page, pageSize);
        var q = _context.Users.AsNoTracking();
        var total = await q.LongCountAsync(cancellationToken);
        var items = await q
            .OrderBy(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Skip((p - 1) * s)
            .Take(s)
            .Select(u => new UserPageRow(
                u.Id,
                u.Name,
                u.UserName ?? "",
                u.Email,
                u.CreatedAt))
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<(List<UserPageRow> Items, long TotalCount)> SearchByNamePagedAsync(
        string nameContains,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.Normalize(page, pageSize);
        var q = _context.Users.AsNoTracking().Where(u => u.Name.Contains(nameContains));
        var total = await q.LongCountAsync(cancellationToken);
        var items = await q
            .OrderBy(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Skip((p - 1) * s)
            .Take(s)
            .Select(u => new UserPageRow(
                u.Id,
                u.Name,
                u.UserName ?? "",
                u.Email,
                u.CreatedAt))
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<(List<UserPageRow> Items, long TotalCount)> SearchByUserNamePagedAsync(
        string userNameContains,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.Normalize(page, pageSize);
        var q = _context.Users.AsNoTracking()
            .Where(u => u.UserName != null && u.UserName.Contains(userNameContains));
        var total = await q.LongCountAsync(cancellationToken);
        var items = await q
            .OrderBy(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Skip((p - 1) * s)
            .Take(s)
            .Select(u => new UserPageRow(
                u.Id,
                u.Name,
                u.UserName ?? "",
                u.Email,
                u.CreatedAt))
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    /// <inheritdoc />
    public async Task<Dictionary<Guid, List<string>>> GetRoleNamesByUserIdsAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, List<string>>();
        }

        // Một lần join AspNetUserRoles + AspNetRoles thay cho N lần UserManager.GetRolesAsync.
        var rows = await (
            from ur in _context.UserRoles.AsNoTracking()
            join r in _context.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where userIds.Contains(ur.UserId)
            select new { ur.UserId, RoleName = r.Name }
        ).ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.RoleName).Where(n => n != null).Cast<string>().OrderBy(n => n).ToList());
    }

    public async Task<User?> GetByIdAsync(Guid id)
    {
        return await _context.Users.FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task AddAsync(User user)
    {
        await _context.Users.AddAsync(user);
    }

    public void Update(User user)
    {
        _context.Users.Update(user);
    }

    public void Remove(User user)
    {
        _context.Users.Remove(user);
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _context.Users.AsNoTracking().AnyAsync(x => x.Id == id);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}
