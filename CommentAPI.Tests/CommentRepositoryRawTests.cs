using CommentAPI.Data;
using CommentAPI.Entities;
using CommentAPI.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Tests;

public class CommentRepositoryRawTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"comment-repo-tests-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static CommentRepository CreateSut(AppDbContext db)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        return new CommentRepository(db, accessor);
    }

    private static (Guid PostA, Guid PostB, Comment RootA, Comment ChildA1, Comment ChildA2, Comment RootB) Seed(AppDbContext db)
    {
        var postA = Guid.NewGuid();
        var postB = Guid.NewGuid();
        var user = Guid.NewGuid();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var rootA = new Comment { Id = Guid.NewGuid(), PostId = postA, UserId = user, ParentId = null, Content = "root-a", CreatedAt = t0 };
        var childA1 = new Comment { Id = Guid.NewGuid(), PostId = postA, UserId = user, ParentId = rootA.Id, Content = "child-a1", CreatedAt = t0.AddMinutes(1) };
        var childA2 = new Comment { Id = Guid.NewGuid(), PostId = postA, UserId = user, ParentId = childA1.Id, Content = "child-a2", CreatedAt = t0.AddMinutes(2) };
        var rootB = new Comment { Id = Guid.NewGuid(), PostId = postB, UserId = user, ParentId = null, Content = "root-b", CreatedAt = t0.AddMinutes(3) };

        db.Comments.AddRange(rootA, childA1, childA2, rootB);
        db.SaveChanges();
        return (postA, postB, rootA, childA1, childA2, rootB);
    }

    [Fact]
    public async Task RR01_LoadRawFlatAsync_ShouldReturnPagedAll_WhenRootsOnlyFalse()
    {
        using var db = CreateDb();
        var seeded = Seed(db);
        var sut = CreateSut(db);

        var (items, total, related) = await sut.LoadRawFlatAsync(null, 1, 10, rootsOnly: false, loadCommentsForRootPosts: false);

        Assert.Equal(4, total);
        Assert.Equal(4, items.Count);
        Assert.Empty(related);
        Assert.Contains(items, x => x.Id == seeded.ChildA2.Id);
    }

    [Fact]
    public async Task RR02_LoadRawFlatAsync_ShouldReturnOnlyRoots_WhenRootsOnlyTrue()
    {
        using var db = CreateDb();
        Seed(db);
        var sut = CreateSut(db);

        var (items, total, _) = await sut.LoadRawFlatAsync(null, 1, 10, rootsOnly: true, loadCommentsForRootPosts: false);

        Assert.Equal(2, total);
        Assert.All(items, x => Assert.Null(x.ParentId));
    }

    [Fact]
    public async Task RR03_LoadRawFlatAsync_ShouldLoadRelatedComments_WhenEnabled()
    {
        using var db = CreateDb();
        var seeded = Seed(db);
        var sut = CreateSut(db);

        var (roots, total, related) = await sut.LoadRawFlatAsync(
            seeded.PostA,
            1,
            10,
            rootsOnly: true,
            loadCommentsForRootPosts: true);

        Assert.Single(roots);
        Assert.Equal(1, total);
        Assert.Equal(3, related.Count);
        Assert.Contains(related, x => x.Id == seeded.ChildA2.Id);
    }

    [Fact]
    public async Task RR04_LoadRawFlatAsync_ShouldApplyDateRange_OnRootQuery()
    {
        using var db = CreateDb();
        var seeded = Seed(db);
        var sut = CreateSut(db);

        var from = seeded.RootB.CreatedAt.AddSeconds(-1);
        var to = seeded.RootB.CreatedAt.AddSeconds(1);

        var (roots, total, _) = await sut.LoadRawFlatAsync(null, 1, 10, true, false, default, from, to);

        Assert.Single(roots);
        Assert.Equal(1, total);
        Assert.Equal(seeded.RootB.Id, roots[0].Id);
    }

    [Fact]
    public async Task RR05_LoadRawCteAsync_ShouldBuildFlatRowsWithLevels_ForOnePost()
    {
        using var db = CreateDb();
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
        using var db = CreateDb();
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
        using var db = CreateDb();
        Seed(db);
        var sut = CreateSut(db);

        var rows = await sut.LoadRawCteAsync(null);

        Assert.Equal(4, rows.Count);
        Assert.Equal(2, rows.Count(x => x.Level == 0));
    }
}

