using CommentAPI.Data;
using CommentAPI.Entities;
using CommentAPI.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Tests;

public class CommentRepositoryRawTests
{
    // InMemory: LoadFlat và các nhánh EF không cần SqlQueryRaw.
    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"comment-repo-tests-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    // SQLite trong RAM: bật FK + schema quan hệ; LoadRawCteAsync (SqlQueryRaw) cần provider SQL thật.
    private static AppDbContext CreateSqliteDb()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static CommentRepository CreateSut(AppDbContext db) => new(db);

    private static (Guid PostA, Guid PostB, Comment RootA, Comment ChildA1, Comment ChildA2, Comment RootB) Seed(AppDbContext db)
    {
        var postA = Guid.NewGuid();
        var postB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // SQLite kiểm tra FK: tạo user + bài trước khi gắn comment.
        var user = new User
        {
            Id = userId,
            UserName = "repo-test",
            NormalizedUserName = "REPO-TEST",
            Email = "repo@test.local",
            NormalizedEmail = "REPO@TEST.LOCAL",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Name = "tester",
            CreatedAt = t0
        };
        db.Users.Add(user);
        db.Posts.AddRange(
            new Post { Id = postA, UserId = userId, Title = "a", Content = "a", CreatedAt = t0 },
            new Post { Id = postB, UserId = userId, Title = "b", Content = "b", CreatedAt = t0 });
        db.SaveChanges();

        var rootA = new Comment { Id = Guid.NewGuid(), PostId = postA, UserId = userId, ParentId = null, Content = "root-a", CreatedAt = t0 };
        var childA1 = new Comment { Id = Guid.NewGuid(), PostId = postA, UserId = userId, ParentId = rootA.Id, Content = "child-a1", CreatedAt = t0.AddMinutes(1) };
        var childA2 = new Comment { Id = Guid.NewGuid(), PostId = postA, UserId = userId, ParentId = childA1.Id, Content = "child-a2", CreatedAt = t0.AddMinutes(2) };
        var rootB = new Comment { Id = Guid.NewGuid(), PostId = postB, UserId = userId, ParentId = null, Content = "root-b", CreatedAt = t0.AddMinutes(3) };

        db.Comments.AddRange(rootA, childA1, childA2, rootB);
        db.SaveChanges();
        return (postA, postB, rootA, childA1, childA2, rootB);
    }

    // F.I.R.S.T: LoadFlatAsync [1][8][10][12] — phân trang phẳng mọi comment (gốc + con) trên một trang.
    // 3A — Arrange: seed 4 comment. Act: trang 1. Assert: đủ 4 dòng, có reply sâu nhất.
    [Fact]
    public async Task RR01_LoadFlatAsync_ShouldReturnPagedFlat_AllCommentsLikeNormalList()
    {
        using var db = CreateInMemoryDb();
        var seeded = Seed(db);
        var sut = CreateSut(db);

        var (items, total) = await sut.LoadFlatAsync(null, 1, 10);

        Assert.Equal(4, total);
        Assert.Equal(4, items.Count);
        Assert.Contains(items, x => x.Id == seeded.ChildA2.Id);
    }

    // F.I.R.S.T: lọc theo PostId — chỉ comment thuộc bài A, thứ tự phẳng bình thường.
    // 3A — Arrange: PostA có 3 comment. Act: LoadFlatAsync(PostA). Assert: total 3, cả con cháu.
    [Fact]
    public async Task RR02_LoadFlatAsync_ShouldFilterByPostId_IncludingReplies()
    {
        using var db = CreateInMemoryDb();
        var seeded = Seed(db);
        var sut = CreateSut(db);

        var (items, total) = await sut.LoadFlatAsync(seeded.PostA, 1, 10);

        Assert.Equal(3, total);
        Assert.Equal(3, items.Count);
        Assert.All(items, x => Assert.Equal(seeded.PostA, x.PostId));
        Assert.Contains(items, x => x.Id == seeded.ChildA2.Id);
    }

    // F.I.R.S.T: phân trang phẳng — trang 2 với pageSize=2 lấy đúng 2 dòng tiếp theo theo PostId,CreatedAt,Id.
    // 3A — Arrange: 4 comment. Act: page 2 size 2. Assert: 2 mục, tổng vẫn 4.
    [Fact]
    public async Task RR03_LoadFlatAsync_ShouldPageFlatRows_NotRootsOnly()
    {
        using var db = CreateInMemoryDb();
        Seed(db);
        var sut = CreateSut(db);

        var (items, total) = await sut.LoadFlatAsync(null, 2, 2);

        Assert.Equal(4, total);
        Assert.Equal(2, items.Count);
    }

    // F.I.R.S.T: khoảng CreatedAt áp trên mọi dòng phẳng (không chỉ gốc).
    // 3A — Arrange: chỉ RootB nằm trong khoảng thời gian hẹp. Act: query. Assert: một dòng đúng RootB.
    [Fact]
    public async Task RR04_LoadFlatAsync_ShouldApplyDateRange_OnFlatQuery()
    {
        using var db = CreateInMemoryDb();
        var seeded = Seed(db);
        var sut = CreateSut(db);

        var from = seeded.RootB.CreatedAt.AddSeconds(-1);
        var to = seeded.RootB.CreatedAt.AddSeconds(1);

        var (items, total) = await sut.LoadFlatAsync(null, 1, 10, default, from, to);

        Assert.Single(items);
        Assert.Equal(1, total);
        Assert.Equal(seeded.RootB.Id, items[0].Id);
    }

    [Fact]
    public async Task RR05_LoadRawCteAsync_ShouldBuildFlatRowsWithLevels_ForOnePost()
    {
        using var db = CreateSqliteDb();
        var seeded = Seed(db);
        var sut = CreateSut(db);

        var rows = await sut.LoadRawCteAsync(seeded.PostA);

        Assert.Equal(3, rows.Count);
        Assert.Equal(0, rows.Single(x => x.Id == seeded.RootA.Id).Level);
        Assert.Equal(1, rows.Single(x => x.Id == seeded.ChildA1.Id).Level);
        Assert.Equal(2, rows.Single(x => x.Id == seeded.ChildA2.Id).Level);
    }

    [Fact]
    public async Task RR06_LoadRawCteAsync_ShouldApplyDateFilter_OnRows()
    {
        using var db = CreateSqliteDb();
        var seeded = Seed(db);
        var sut = CreateSut(db);

        var rows = await sut.LoadRawCteAsync(
            seeded.PostA,
            default,
            createdAtFrom: seeded.ChildA1.CreatedAt,
            createdAtTo: seeded.ChildA2.CreatedAt);

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, x => x.Id == seeded.RootA.Id);
        Assert.Contains(rows, x => x.Id == seeded.ChildA2.Id);
    }

    [Fact]
    public async Task RR07_LoadRawCteAsync_ShouldReturnAllPosts_WhenPostIdNull()
    {
        using var db = CreateSqliteDb();
        Seed(db);
        var sut = CreateSut(db);

        var rows = await sut.LoadRawCteAsync(null);

        Assert.Equal(4, rows.Count);
        Assert.Equal(2, rows.Count(x => x.Level == 0));
    }

    // F.I.R.S.T: kiểm tra lọc UserId trên kết quả CTE.
    // 3A — Arrange: seed có comment; Act: userId không khớp ai; Assert: không còn dòng.
    [Fact]
    public async Task RR08_LoadRawCteAsync_ShouldReturnEmpty_WhenUserIdMatchesNobody()
    {
        using var db = CreateSqliteDb();
        var seeded = Seed(db);
        var sut = CreateSut(db);

        var rows = await sut.LoadRawCteAsync(seeded.PostA, default, null, null, Guid.NewGuid(), null);

        Assert.Empty(rows);
    }

    // F.I.R.S.T: phân trang gốc — trang 1 cỡ 1 chỉ trả một comment ParentId null.
    // 3A — Arrange: 2 gốc (PostA, PostB). Act: page 1 size 1. Assert: TotalRootCount 2, Items một gốc.
    [Fact]
    public async Task RR09_GetCommentRootsRoutePagedAsync_ShouldPageRootsOnly()
    {
        using var db = CreateInMemoryDb();
        Seed(db);
        var sut = CreateSut(db);

        var (items, totalRoots) = await sut.GetCommentRootsRoutePagedAsync(null, null, 1, 1);

        Assert.Equal(2, totalRoots);
        Assert.Single(items);
        Assert.Null(items[0].ParentId);
    }

    // F.I.R.S.T: subtree SQL — một gốc trả đủ gốc + con + cháu cùng PostId.
    // 3A — Arrange: PostA có 3 tầng. Act: LoadCommentsForSubtreesAsync([RootA]). Assert: 3 hàng.
    [Fact]
    public async Task RR10_LoadCommentsForSubtreesAsync_ShouldReturnFullThread()
    {
        using var db = CreateSqliteDb();
        var seeded = Seed(db);
        var sut = CreateSut(db);

        var rows = await sut.LoadCommentsForSubtreesAsync(new[] { seeded.RootA.Id });

        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, x => x.Id == seeded.RootA.Id);
        Assert.Contains(rows, x => x.Id == seeded.ChildA2.Id);
    }

    // F.I.R.S.T: GetAllCommentsForPost — CTE riêng SqlQueryRaw (SQLite); đủ cây + Level khi true; chỉ gốc Level 0 khi false.
    // 3A — Arrange: Seed PostA 3 tầng. Act: GetAllCommentsForPost(postA, true/false). Assert: 3 dòng Level 0,1,2 vs 1 dòng gốc.
    [Fact]
    public async Task RR11_GetAllCommentsForPost_ShouldRespectIncludeReplies()
    {
        using var db = CreateSqliteDb();
        var seeded = Seed(db);
        var sut = CreateSut(db);

        var all = await sut.GetAllCommentsForPost(seeded.PostA, includeReplies: true);
        var rootsOnly = await sut.GetAllCommentsForPost(seeded.PostA, includeReplies: false);

        Assert.Equal(3, all.Count);
        Assert.Equal(0, all.Single(x => x.Id == seeded.RootA.Id).Level);
        Assert.Equal(1, all.Single(x => x.Id == seeded.ChildA1.Id).Level);
        Assert.Equal(2, all.Single(x => x.Id == seeded.ChildA2.Id).Level);
        Assert.Single(rootsOnly);
        Assert.Null(rootsOnly[0].ParentId);
        Assert.Equal(0, rootsOnly[0].Level);
        Assert.Equal(seeded.RootA.Id, rootsOnly[0].Id);
    }

    // F.I.R.S.T: biên — bài tồn tại nhưng không có comment; CTE vẫn chạy, kết quả rỗng.
    // 3A — Arrange: Seed + thêm PostEmpty không có dòng Comments. Act: GetAllCommentsForPost(emptyPost, true). Assert: list rỗng.
    [Fact]
    public async Task RR12_GetAllCommentsForPost_ShouldReturnEmpty_WhenPostHasNoComments()
    {
        using var db = CreateSqliteDb();
        var seeded = Seed(db);
        var userId = await db.Users.Select(u => u.Id).FirstAsync();
        var emptyPostId = Guid.NewGuid();
        db.Posts.Add(new Post { Id = emptyPostId, UserId = userId, Title = "no-comments", Content = "x", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var rows = await sut.GetAllCommentsForPost(emptyPostId, includeReplies: true);

        Assert.Empty(rows);
    }

    // F.I.R.S.T: cô lập PostId — chỉ hàng thuộc đúng bài được trả về.
    // 3A — Arrange: PostB chỉ có một gốc. Act: GetAllCommentsForPost(PostB, true). Assert: một dòng, khớp RootB; không lẫn PostA.
    [Fact]
    public async Task RR13_GetAllCommentsForPost_ShouldReturnOnlyCommentsForRequestedPost()
    {
        using var db = CreateSqliteDb();
        var seeded = Seed(db);
        var sut = CreateSut(db);

        var rowsB = await sut.GetAllCommentsForPost(seeded.PostB, includeReplies: true);
        var rowsA = await sut.GetAllCommentsForPost(seeded.PostA, includeReplies: true);

        Assert.Single(rowsB);
        Assert.Equal(seeded.RootB.Id, rowsB[0].Id);
        Assert.Equal(0, rowsB[0].Level);
        Assert.Equal(3, rowsA.Count);
        Assert.DoesNotContain(rowsA, x => x.Id == seeded.RootB.Id);
    }
}

