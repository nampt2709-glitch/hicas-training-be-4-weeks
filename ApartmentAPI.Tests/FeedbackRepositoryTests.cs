using ApartmentAPI;
using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using ApartmentAPI.Repositories;
using Microsoft.AspNetCore.Http;

namespace ApartmentAPI.Tests;

// Kiểm thử FeedbackRepository — cây phản hồi, phân trang, cặp Id/ParentId.
public class FeedbackRepositoryTests
{
    private static FeedbackListSort Sort() => new(FeedbackSortColumn.CreatedAt, false);

    // F.I.R.S.T — lọc rootsOnly và sắp ghim.
    // 3A — Arrange gốc + trả lời; Act: GetRootsAsync; Assert: chỉ ParentId null, ghim trước.
    [Fact]
    public async Task FR01_GetRootsAsync_ShouldExcludeReplies_AndOrderPinnedFirst()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new FeedbackRepository(db);
        var user = await ApartmentTestData.AddUserAsync(db, 1);
        var rootPlain = await ApartmentTestData.AddFeedbackAsync(db, user.Id, null, 1, pinned: false);
        var rootPinned = await ApartmentTestData.AddFeedbackAsync(db, user.Id, null, 2, pinned: true);
        await ApartmentTestData.AddFeedbackAsync(db, user.Id, rootPlain.Id, 3);

        var roots = await sut.GetRootsAsync();

        Assert.Equal(2, roots.Count);
        Assert.Equal(rootPinned.Id, roots[0].Id);
        Assert.All(roots, r => Assert.Null(r.ParentId));
    }

    // F.I.R.S.T — GetByUserIdAsync chỉ feedback của user.
    // 3A — Arrange hai user; Act: GetByUserIdAsync(userA); Assert: chỉ bài của A.
    [Fact]
    public async Task FR02_GetByUserIdAsync_ShouldFilterByAuthor()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new FeedbackRepository(db);
        var a = await ApartmentTestData.AddUserAsync(db, 10);
        var b = await ApartmentTestData.AddUserAsync(db, 11);
        await ApartmentTestData.AddFeedbackAsync(db, a.Id, null, 1);
        var fbB = await ApartmentTestData.AddFeedbackAsync(db, b.Id, null, 2);

        var list = await sut.GetByUserIdAsync(b.Id);

        var single = Assert.Single(list);
        Assert.Equal(fbB.Id, single.Id);
    }

    // F.I.R.S.T — GetPagedAsync rootsOnly.
    // 3A — Arrange gốc + con; Act: rootsOnly true; Assert: một dòng.
    [Fact]
    public async Task FR03_GetPagedAsync_ShouldHonorRootsOnly()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new FeedbackRepository(db);
        var user = await ApartmentTestData.AddUserAsync(db, 20);
        var root = await ApartmentTestData.AddFeedbackAsync(db, user.Id, null, 1);
        await ApartmentTestData.AddFeedbackAsync(db, user.Id, root.Id, 2);

        var (items, total, _, _) = await sut.GetPagedAsync(1, 10, null, null, null, true, null, Sort());

        Assert.Equal(1, total);
        Assert.Equal(root.Id, items[0].Id);
    }

    // F.I.R.S.T — lọc theo userId trên phân trang.
    [Fact]
    public async Task FR04_GetPagedAsync_ShouldFilterByUserId()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new FeedbackRepository(db);
        var u1 = await ApartmentTestData.AddUserAsync(db, 30);
        var u2 = await ApartmentTestData.AddUserAsync(db, 31);
        await ApartmentTestData.AddFeedbackAsync(db, u1.Id, null, 1);
        var f2 = await ApartmentTestData.AddFeedbackAsync(db, u2.Id, null, 2);

        var (items, total, _, _) = await sut.GetPagedAsync(1, 10, null, null, u2.Id, false, null, Sort());

        Assert.Equal(1, total);
        Assert.Equal(f2.Id, items[0].Id);
    }

    // F.I.R.S.T — Contains nội dung.
    [Fact]
    public async Task FR05_GetPagedAsync_ShouldFilterContentContains()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new FeedbackRepository(db);
        var user = await ApartmentTestData.AddUserAsync(db, 40);
        await ApartmentTestData.AddFeedbackAsync(db, user.Id, null, 1);
        var fMatch = new Feedback
        {
            Content = "Từ khóa đặc biệt XYZ-123",
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
        };
        db.Feedbacks.Add(fMatch);
        await db.SaveChangesAsync();

        var (items, total, _, _) = await sut.GetPagedAsync(1, 10, null, null, null, false, "XYZ-123", Sort());

        Assert.Equal(1, total);
        Assert.Equal(fMatch.Id, items[0].Id);
    }

    // F.I.R.S.T — khoảng ngày CreatedAt.
    [Fact]
    public async Task FR06_GetPagedAsync_ShouldFilterCreatedAtRange()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new FeedbackRepository(db);
        var user = await ApartmentTestData.AddUserAsync(db, 50);
        var inside = new Feedback
        {
            Content = "Trong khoảng",
            UserId = user.Id,
            CreatedAt = new DateTime(2025, 5, 10, 0, 0, 0, DateTimeKind.Utc),
        };
        var outside = new Feedback
        {
            Content = "Ngoài khoảng",
            UserId = user.Id,
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        db.Feedbacks.AddRange(inside, outside);
        await db.SaveChangesAsync();

        var from = new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 5, 31, 0, 0, 0, DateTimeKind.Utc);
        var (items, total, _, _) = await sut.GetPagedAsync(1, 10, from, to, null, false, null, Sort());

        Assert.Equal(1, total);
        Assert.Equal(inside.Id, items[0].Id);
    }

    // F.I.R.S.T — GetAllIdParentPairsAsync trả đủ nút còn hiệu lực.
    [Fact]
    public async Task FR07_GetAllIdParentPairsAsync_ShouldListPairs()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new FeedbackRepository(db);
        var user = await ApartmentTestData.AddUserAsync(db, 60);
        var root = await ApartmentTestData.AddFeedbackAsync(db, user.Id, null, 1);
        var child = await ApartmentTestData.AddFeedbackAsync(db, user.Id, root.Id, 2);

        var pairs = await sut.GetAllIdParentPairsAsync();

        Assert.Contains(pairs, p => p.Id == root.Id && p.ParentId == null);
        Assert.Contains(pairs, p => p.Id == child.Id && p.ParentId == root.Id);
    }

    // F.I.R.S.T — SoftDelete không tìm thấy.
    [Fact]
    public async Task FR08_SoftDeleteAsync_ShouldThrow_WhenMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new FeedbackRepository(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.SoftDeleteAsync(Guid.NewGuid(), "x"));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
    }

    // F.I.R.S.T — GetById sau xóa mềm ẩn khỏi query.
    [Fact]
    public async Task FR09_GetByIdAsync_ShouldBeNull_WhenSoftDeleted()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new FeedbackRepository(db);
        var user = await ApartmentTestData.AddUserAsync(db, 70);
        var fb = await ApartmentTestData.AddFeedbackAsync(db, user.Id, null, 1);
        fb.IsDeleted = true;
        fb.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Assert.Null(await sut.GetByIdAsync(fb.Id));
    }

    // F.I.R.S.T — ExistsAsync false khi đã xóa mềm.
    [Fact]
    public async Task FR10_ExistsAsync_ShouldBeFalse_WhenSoftDeleted()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new FeedbackRepository(db);
        var user = await ApartmentTestData.AddUserAsync(db, 71);
        var fb = await ApartmentTestData.AddFeedbackAsync(db, user.Id, null, 2);
        fb.IsDeleted = true;
        fb.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Assert.False(await sut.ExistsAsync(fb.Id));
    }
}
