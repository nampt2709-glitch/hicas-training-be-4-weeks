using ApartmentAPI.Data; // AppDbContext.
using ApartmentAPI.Entities; // Post.
using Microsoft.EntityFrameworkCore; // EF Core.

namespace ApartmentAPI.Repositories;

// Hợp đồng repository Post: CRUD + phân trang lọc tác giả / căn hộ / tiêu đề / xuất bản.
public interface IPostRepository
{ // Mở khối IPostRepository.
    Task<List<Post>> GetAllAsync(CancellationToken ct = default);

    Task<(List<Post> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        Guid? apartmentId,
        bool? isPublished,
        string? titleContains,
        PostListSort sort,
        CancellationToken ct = default);

    Task<Post?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Post?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(Post entity, CancellationToken ct = default);
    void Update(Post entity);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
} // Kết thúc IPostRepository.

// Truy vấn bảng Post — phân trang, sort whitelist.
public sealed class PostRepository : RepositoryBase<Post>, IPostRepository
{ // Mở khối PostRepository.
    public PostRepository(AppDbContext context)
        : base(context)
    { // Constructor — base gán DbContext.
    } // Kết thúc constructor.

    public override Task<List<Post>> GetAllAsync(CancellationToken ct = default) =>
        base.GetAllAsync(ct);

    public async Task<(List<Post> Items, long TotalCount, int Page, int PageSize)> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        Guid? apartmentId,
        bool? isPublished,
        string? titleContains,
        PostListSort sort,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        var (p, s) = PaginationQuery.Normalize(page, pageSize);
        var q = Set.AsNoTracking().AsQueryable();
        q = q.WhereCreatedAtRange(createdAtFrom, createdAtTo);
        if (userId is { } uid)
            q = q.Where(x => x.UserId == uid);
        if (apartmentId is { } aid)
            q = q.Where(x => x.ApartmentId == aid);
        if (isPublished is { } pub)
            q = q.Where(x => x.IsPublished == pub);
        var title = titleContains?.Trim();
        if (!string.IsNullOrEmpty(title))
            q = q.Where(x => x.Title.Contains(title));
        var total = await q.LongCountAsync(ct);
        q = ApplyPostSort(q, sort);
        var items = await q.Skip((p - 1) * s).Take(s).ToListAsync(ct);
        return (items, total, p, s);
    } // Kết thúc GetPagedAsync.

    private static IQueryable<Post> ApplyPostSort(IQueryable<Post> q, PostListSort spec)
    { // Mở khối ApplyPostSort.
        var desc = spec.Descending;
        return spec.Column switch
        {
            PostSortColumn.Id => desc ? q.OrderByDescending(x => x.Id) : q.OrderBy(x => x.Id),
            PostSortColumn.UserId => desc ? q.OrderByDescending(x => x.UserId) : q.OrderBy(x => x.UserId),
            PostSortColumn.ApartmentId => desc ? q.OrderByDescending(x => x.ApartmentId) : q.OrderBy(x => x.ApartmentId),
            PostSortColumn.Title => desc ? q.OrderByDescending(x => x.Title) : q.OrderBy(x => x.Title),
            PostSortColumn.IsPublished => desc ? q.OrderByDescending(x => x.IsPublished) : q.OrderBy(x => x.IsPublished),
            PostSortColumn.CreatedAt => desc ? q.OrderByDescending(x => x.CreatedAt) : q.OrderBy(x => x.CreatedAt),
            _ => desc ? q.OrderByDescending(x => x.CreatedAt) : q.OrderBy(x => x.CreatedAt),
        };
    } // Kết thúc ApplyPostSort.

    public override Task<Post?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        base.GetByIdAsync(id, ct);

    public override Task<Post?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) =>
        base.GetByIdTrackedAsync(id, ct);

    public override Task AddAsync(Post entity, CancellationToken ct = default) =>
        base.AddAsync(entity, ct);

    public override void Update(Post entity) =>
        base.Update(entity);

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        var entity = await GetByIdTrackedAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Post not found.");
        SoftDelete(entity, deletedBy);
    } // Kết thúc SoftDeleteAsync.

    public override Task SaveChangesAsync(CancellationToken ct = default) =>
        base.SaveChangesAsync(ct);

    public override Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) =>
        base.ExistsAsync(id, ct);
} // Kết thúc PostRepository.
