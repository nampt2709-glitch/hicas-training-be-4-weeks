using CommentAPI.Data;
using CommentAPI.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.RecordGenerator;

/// <summary>
/// Sinh và xóa lô ~100k bản ghi (Users, Posts, Comments) với dữ liệu có vẻ thực tế; cleanup tôn trọng FK.
/// </summary>
internal sealed class BulkRecordGenerator
{
    private readonly AppDbContext _db;
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;

    public BulkRecordGenerator(
        AppDbContext db,
        UserManager<User> userManager,
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    /// <summary>
    /// Kiểm tra xem đã có user bulk (hậu tố email chuẩn hóa) hay chưa — nếu có thì không insert trùng.
    /// </summary>
    private Task<bool> AnyBulkUsersExistAsync(CancellationToken ct)
    {
        // Dùng EF.Functions.Like — EF Core không dịch EndsWith(..., StringComparison) sang SQL.
        var pattern = "%" + BulkGenerationConstants.NormalizedEmailSuffix;
        return _db.Users.AsNoTracking().AnyAsync(
            u => u.NormalizedEmail != null && EF.Functions.Like(u.NormalizedEmail, pattern),
            ct);
    }

    /// <summary>
    /// Chèn marker + user nội dung, post, comment theo batch; bỏ qua nếu lô đã tồn tại.
    /// </summary>
    public async Task InsertIfNeededAsync(CancellationToken ct)
    {
        if (await AnyBulkUsersExistAsync(ct))
        {
            Console.WriteLine("Đã phát hiện dữ liệu bulk (@bulkgen.recordgenerator.local). Bỏ qua insert. Chạy cleanup trước nếu muốn tạo lại.");
            return;
        }

        var rnd = new Random(918273645);
        var roleUser = await _roleManager.FindByNameAsync("User")
                       ?? throw new InvalidOperationException("Vai trò User chưa tồn tại. Chạy migration/seed trước.");
        var roleAdmin = await _roleManager.FindByNameAsync("Admin")
                        ?? throw new InvalidOperationException("Vai trò Admin chưa tồn tại. Chạy migration/seed trước.");

        // Tạo user marker trước để có Id cố định trong lô; không dùng làm tác giả comment.
        var marker = new User
        {
            UserName = BulkGenerationConstants.MarkerUserName,
            Email = "bulkgen.marker@bulkgen.recordgenerator.local",
            EmailConfirmed = true,
            Name = "Hệ thống đánh dấu bulk",
            CreatedAt = DateTime.UtcNow
        };

        var markerResult = await _userManager.CreateAsync(marker, BulkGenerationConstants.BulkUserPassword);
        if (!markerResult.Succeeded)
        {
            throw new InvalidOperationException("Không tạo được marker: " + string.Join("; ", markerResult.Errors.Select(e => e.Description)));
        }

        await _userManager.AddToRoleAsync(marker, roleUser.Name!);

        var authorIds = new List<Guid>(BulkGenerationConstants.UserCount);

        // Tạo các tài khoản nội dung (không gồm marker) để gán bài và bình luận.
        // Admin: tiền tố bulkgen_a_; user thường: bulkgen_u_ (chỉ số u bắt đầu lại từ 1 sau khối admin).
        for (var i = 1; i < BulkGenerationConstants.UserCount; i++)
        {
            string userName;
            string email;
            if (i <= BulkGenerationConstants.BulkAdminCount)
            {
                userName = $"bulkgen_a_{i:D5}";
                email = $"bulkgen_a_{i:D5}@bulkgen.recordgenerator.local";
            }
            else
            {
                var plainUserIndex = i - BulkGenerationConstants.BulkAdminCount;
                userName = $"bulkgen_u_{plainUserIndex:D5}";
                email = $"bulkgen_u_{plainUserIndex:D5}@bulkgen.recordgenerator.local";
            }

            var u = new User
            {
                UserName = userName,
                Email = email,
                EmailConfirmed = true,
                Name = BuildPersonName(rnd, i),
                CreatedAt = DateTime.UtcNow.AddDays(-rnd.Next(0, 520))
            };

            var r = await _userManager.CreateAsync(u, BulkGenerationConstants.BulkUserPassword);
            if (!r.Succeeded)
            {
                throw new InvalidOperationException($"Không tạo user {u.UserName}: " + string.Join("; ", r.Errors.Select(e => e.Description)));
            }

            // Mọi tài khoản nội dung là User; N user đầu tiên thêm Admin để có ít nhất số admin cấu hình.
            await _userManager.AddToRoleAsync(u, roleUser.Name!);
            if (i <= BulkGenerationConstants.BulkAdminCount)
            {
                await _userManager.AddToRoleAsync(u, roleAdmin.Name!);
            }

            authorIds.Add(u.Id);
        }

        Console.WriteLine(
            $"Đã tạo {BulkGenerationConstants.UserCount} user Identity (gồm marker), trong đó {Math.Min(BulkGenerationConstants.BulkAdminCount, authorIds.Count)} tài khoản có vai trò Admin. Đang tạo bài viết…");

        // Gom post trong bộ nhớ rồi AddRange theo batch để giảm round-trip SQL.
        var posts = new List<Post>(BulkGenerationConstants.PostCount);
        for (var p = 0; p < BulkGenerationConstants.PostCount; p++)
        {
            var userId = authorIds[p % authorIds.Count];
            posts.Add(new Post
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = BuildPostTitle(rnd, p),
                Content = BuildPostContent(rnd, p),
                CreatedAt = DateTime.UtcNow.AddHours(-rnd.Next(0, 9000))
            });
        }

        foreach (var chunk in posts.Chunk(800))
        {
            _db.Posts.AddRange(chunk);
            await _db.SaveChangesAsync(ct);
        }

        Console.WriteLine($"Đã tạo {BulkGenerationConstants.PostCount} bài. Đang tạo bình luận (có thread)…");

        var commentsPerPost = BulkGenerationConstants.CommentCount / BulkGenerationConstants.PostCount;
        var commentBuffer = new List<Comment>(4000);

        foreach (var post in posts)
        {
            var idsOnPost = new List<Guid>(commentsPerPost);
            for (var c = 0; c < commentsPerPost; c++)
            {
                var newId = Guid.NewGuid();
                Guid? parentId = null;

                // Một phần comment là trả lời để tạo cây hợp lý trong cùng PostId.
                if (idsOnPost.Count > 0 && rnd.NextDouble() < 0.33)
                {
                    parentId = idsOnPost[rnd.Next(idsOnPost.Count)];
                }

                idsOnPost.Add(newId);
                commentBuffer.Add(new Comment
                {
                    Id = newId,
                    PostId = post.Id,
                    UserId = authorIds[rnd.Next(authorIds.Count)],
                    ParentId = parentId,
                    Content = BuildCommentContent(rnd, c),
                    CreatedAt = post.CreatedAt.AddMinutes(rnd.Next(3, 200_000))
                });

                if (commentBuffer.Count >= 3500)
                {
                    _db.Comments.AddRange(commentBuffer);
                    await _db.SaveChangesAsync(ct);
                    commentBuffer.Clear();
                }
            }
        }

        if (commentBuffer.Count > 0)
        {
            _db.Comments.AddRange(commentBuffer);
            await _db.SaveChangesAsync(ct);
        }

        Console.WriteLine(
            $"Hoàn tất: {BulkGenerationConstants.UserCount} users + {BulkGenerationConstants.PostCount} posts + {BulkGenerationConstants.CommentCount} comments = {BulkGenerationConstants.TotalBusinessRows} bản ghi nghiệp vụ.");
    }

    /// <summary>
    /// Xóa comment/post/user thuộc lô bulk: gỡ ParentId trước, rồi xóa theo thứ tự FK; Identity cascade dọn bảng phụ.
    /// </summary>
    public async Task CleanupBulkAsync(CancellationToken ct)
    {
        // Bước 1: bỏ liên kết cha-con trong Comments liên quan post của user bulk (tránh Restrict).
        await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE c SET c.ParentId = NULL
            FROM Comments c
            INNER JOIN Posts p ON c.PostId = p.Id
            INNER JOIN Users u ON p.UserId = u.Id
            WHERE u.NormalizedEmail IS NOT NULL
              AND u.NormalizedEmail LIKE '%@BULKGEN.RECORDGENERATOR.LOCAL'
            """,
            cancellationToken: ct);

        // Bước 2: xóa mọi comment trên post thuộc user bulk.
        var deletedComments = await _db.Database.ExecuteSqlRawAsync(
            """
            DELETE c
            FROM Comments c
            INNER JOIN Posts p ON c.PostId = p.Id
            INNER JOIN Users u ON p.UserId = u.Id
            WHERE u.NormalizedEmail IS NOT NULL
              AND u.NormalizedEmail LIKE '%@BULKGEN.RECORDGENERATOR.LOCAL'
            """,
            cancellationToken: ct);

        // Bước 3: xóa post của user bulk.
        var deletedPosts = await _db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM Posts
            WHERE UserId IN (
                SELECT Id FROM Users
                WHERE NormalizedEmail IS NOT NULL
                  AND NormalizedEmail LIKE '%@BULKGEN.RECORDGENERATOR.LOCAL'
            )
            """,
            cancellationToken: ct);

        // Bước 4: xóa user bulk — các bảng AspNetUser* có cascade phù hợp.
        var deletedUsers = await _db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM Users
            WHERE NormalizedEmail IS NOT NULL
              AND NormalizedEmail LIKE '%@BULKGEN.RECORDGENERATOR.LOCAL'
            """,
            cancellationToken: ct);

        Console.WriteLine($"Cleanup: Comments={deletedComments}, Posts={deletedPosts}, Users={deletedUsers}.");
    }

    private static string BuildPersonName(Random rnd, int seed)
    {
        var first = FirstNames[rnd.Next(FirstNames.Length)];
        var last = LastNames[rnd.Next(LastNames.Length)];
        return $"{first} {last} #{seed:D4}";
    }

    private static string BuildPostTitle(Random rnd, int index)
    {
        var hook = TitleHooks[rnd.Next(TitleHooks.Length)];
        var topic = Topics[rnd.Next(Topics.Length)];
        return $"{hook}: {topic} (bản ghi #{index + 1:N0})";
    }

    private static string BuildPostContent(Random rnd, int index)
    {
        var p1 = Paragraphs[rnd.Next(Paragraphs.Length)];
        var p2 = Paragraphs[rnd.Next(Paragraphs.Length)];
        return $"Mình chia sẻ nhanh chủ đề này sau khi làm việc với API trong tuần.\n\n{p1}\n\n{p2}\n\n(Mã bài nội bộ: {index + 1}, ngẫu nhiên {rnd.Next():N6})";
    }

    private static string BuildCommentContent(Random rnd, int line)
    {
        var phrase = CommentPhrases[rnd.Next(CommentPhrases.Length)];
        return $"{phrase} — #{line + 1} ({rnd.Next(100, 999)})";
    }

    private static readonly string[] FirstNames =
    [
        "Anh", "Bình", "Chi", "Dũng", "Giang", "Hà", "Hải", "Hùng", "Khánh", "Lan",
        "Linh", "Mai", "Minh", "Nam", "Ngọc", "Oanh", "Phúc", "Quang", "Quỳnh", "Tâm",
        "Thảo", "Trang", "Trung", "Tuấn", "Vân", "Việt", "Vy", "Yến"
    ];

    private static readonly string[] LastNames =
    [
        "Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Huỳnh", "Phan", "Vũ", "Võ", "Đặng",
        "Bùi", "Đỗ", "Hồ", "Ngô", "Dương", "Lý"
    ];

    private static readonly string[] TitleHooks =
    [
        "Ghi chú nhanh", "Kinh nghiệm thực tế", "Câu hỏi mở", "Tổng hợp ngắn", "Review sau 2 tuần dùng"
    ];

    private static readonly string[] Topics =
    [
        "tối ưu truy vấn SQL", "phân trang API", "JWT và refresh token", "FluentValidation",
        "EF Core migration", "Swagger versioning", "Redis cache ngắn hạn", "Serilog correlation id",
        "soft delete vs hard delete", "recursive CTE cho comment"
    ];

    private static readonly string[] Paragraphs =
    [
        "Ưu tiên đo thời gian thực tế trên staging trước khi đổi index.",
        "Nên giới hạn độ sâu thread và MAXRECURSION để tránh vòng lặp dữ liệu.",
        "Với SQL Server, batch insert qua AddRange + SaveChanges giúp ổn định bộ nhớ.",
        "Hợp đồng API nên cố định timezone UTC cho CreatedAt.",
        "Kiểm tra quyền theo role trước khi cho phép xóa bài hoặc comment."
    ];

    private static readonly string[] CommentPhrases =
    [
        "Mình đồng ý phần lớn ý chính.",
        "Có thể thêm ví dụ request mẫu không?",
        "Em test trên local thì ổn, production cần log thêm.",
        "Hơi lệch so với tài liệu chính thức một chút.",
        "Cảm ơn bạn, phần này giải thích rõ hơn cho team.",
        "Mình suggest thêm index theo PostId + CreatedAt.",
        "Thread này nên giới hạn độ sâu để UI dễ đọc."
    ];
}
