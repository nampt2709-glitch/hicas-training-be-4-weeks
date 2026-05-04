using CommentAPI.Entities;
using CommentAPI.Data;
using CommentAPI.DTOs;
using CommentAPI.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Repositories;

// Truy vấn bảng Post: danh sách phân trang projection PostDto + đọc một bài theo Id.
public class PostRepository : RepositoryBase<Post>, IPostRepository
{
    #region Trường & hàm tạo

    // BƯỚC 1: Gọi base(context) để gán Context; không cần trường riêng vì đã có Context protected.
    public PostRepository(AppDbContext context)
        : base(context)
    {
    }

    #endregion

    #region Route Functions

    // [1] GET /api/posts — COUNT + một trang PostDto, có lọc CreatedAt / Title / Content.
    public async Task<(List<PostDto> Items, long TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        string? titleContains = null,
        string? contentContains = null)
    {
        // BƯỚC 1: Áp khoảng CreatedAt inclusive qua helper base — dùng EF.Property<DateTime>(..., "CreatedAt").
        var q = ApplyCreatedAtRange(Context.Posts.AsNoTracking(), createdAtFrom, createdAtTo);

        // BƯỚC 2: Lọc tiêu đề Contains nếu chuỗi sau trim không rỗng.
        var t = titleContains?.Trim(); // null-safe trim.
        if (!string.IsNullOrEmpty(t)) // TRƯỜNG HỢP: có từ khóa tiêu đề.
            q = q.Where(p => p.Title.Contains(t)); // SQL WHERE Title LIKE %t% (dịch Contains).

        // BƯỚC 3: Lọc nội dung Contains tương tự.
        var c = contentContains?.Trim();
        if (!string.IsNullOrEmpty(c)) // TRƯỜNG HỢP: có từ khóa nội dung.
            q = q.Where(p => p.Content.Contains(c)); // WHERE Content LIKE %c%.

        // BƯỚC 4: Đếm tổng bản ghi khớp mọi điều kiện — dùng làm TotalCount cho phân trang.
        var total = await q.LongCountAsync(cancellationToken); // COUNT(*) kiểu long.

        // BƯỚC 5: SELECT một trang — mới nhất trước, Id ổn định thứ tự khi CreatedAt trùng.
        var items = await q
            .OrderByDescending(p => p.CreatedAt) // Sắp giảm dần theo thời gian tạo.
            .ThenBy(p => p.Id) // Tie-break PK.
            .Skip((page - 1) * pageSize) // Bỏ các dòng trang trước (page 1-based).
            .Take(pageSize) // Giới hạn số dòng một trang.
            .Select(p => new PostDto // Chiếu ngay trong SQL — chỉ cột cần cho API.
            {
                Id = p.Id,
                Title = p.Title,
                Content = p.Content,
                CreatedAt = p.CreatedAt,
                UserId = p.UserId,
            })
            .ToListAsync(cancellationToken); // Thực thi một round-trip (hoặc vài query tùy provider).

        return (items, total); // Tuple trả lên PostService để gói PagedResult.
    }

    // [2] GET /api/posts/{id} — đọc một bài projection; null nếu không có Id.
    public Task<PostDto?> GetByIdForReadAsync(Guid id, CancellationToken cancellationToken = default) =>
        Context.Posts.AsNoTracking() // Không track — chỉ đọc.
            .Where(p => p.Id == id) // Lọc khóa chính.
            .Select(p => new PostDto // Cùng shape với list để client đồng nhất.
            {
                Id = p.Id,
                Title = p.Title,
                Content = p.Content,
                CreatedAt = p.CreatedAt,
                UserId = p.UserId,
            })
            .FirstOrDefaultAsync(cancellationToken); // 0 hoặc 1 dòng → null hoặc PostDto.

    #endregion

    #region Helpers

    // Helper legacy: toàn bộ entity Post không track — ít dùng trong API hiện tại nhưng giữ cho script/tool.
    public async Task<List<Post>> GetAllAsync()
    {
        return await Context.Posts // DbSet Post.
            .AsNoTracking() // Read-only.
            .OrderByDescending(x => x.CreatedAt) // Mới trước.
            .ToListAsync(); // Materialize toàn bộ — cẩn thận khối lượng lớn trên production.
    }

    #endregion
}
