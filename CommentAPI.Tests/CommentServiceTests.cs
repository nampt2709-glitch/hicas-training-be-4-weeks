using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Entities;
using CommentAPI.Interfaces;
using CommentAPI.Services;
using Microsoft.AspNetCore.Http;
using Moq;

namespace CommentAPI.Tests;

public class CommentServiceTests
{
    private static CommentService CreateSut(Mock<ICommentRepository> repo, Mock<IEntityResponseCache> cache)
        => new(repo.Object, TestMapperFactory.CreateMapper(), cache.Object, EpochMock().Object);

    private static Mock<ICacheListEpochStore> EpochMock()
    {
        var e = new Mock<ICacheListEpochStore>();
        e.Setup(x => x.GetCommentsListEpochAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0L);
        e.Setup(x => x.GetPostsListEpochAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0L);
        e.Setup(x => x.GetUsersListEpochAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0L);
        e.Setup(x => x.InvalidateCommentsListsAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        e.Setup(x => x.InvalidatePostsListAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        e.Setup(x => x.InvalidateUsersListAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return e;
    }

    // F.I.R.S.T: nhanh, độc lập, lặp lại ổn định, tự kiểm chứng, kịp thời.
    // 3A — Arrange: cache có sẵn DTO. Act: gọi GetByIdAsync. Assert: không truy vấn repository.
    [Fact]
    public async Task CM01_GetByIdAsync_ShouldReturnFromCache_WhenCacheHit()
    {
        var id = Guid.NewGuid();
        var cached = new CommentDto { Id = id, Content = "cached", CreatedAt = DateTime.UtcNow, PostId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        var repo = new Mock<ICommentRepository>(MockBehavior.Strict);
        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(x => x.GetJsonAsync<CommentDto>(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(cached);

        var sut = CreateSut(repo, cache);
        var result = await sut.GetByIdAsync(id);

        Assert.Same(cached, result);
    }

    // F.I.R.S.T: xác minh đường cache-miss.
    // 3A — Arrange: cache miss, repo trả DTO. Act: gọi GetByIdAsync. Assert: DTO trả đúng và có Set cache.
    [Fact]
    public async Task CM02_GetByIdAsync_ShouldReadRepository_AndSetCache_WhenCacheMiss()
    {
        var id = Guid.NewGuid();
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.GetCommentByIdRouteReadAsync(id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommentDto { Id = id, Content = "db", CreatedAt = DateTime.UtcNow, PostId = Guid.NewGuid(), UserId = Guid.NewGuid() });

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(x => x.GetJsonAsync<CommentDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CommentDto?)null);
        cache.Setup(x => x.SetJsonAsync(It.IsAny<string>(), It.IsAny<CommentDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(repo, cache);
        var result = await sut.GetByIdAsync(id);

        Assert.Equal(id, result.Id);
        cache.Verify(x => x.SetJsonAsync(It.IsAny<string>(), It.Is<CommentDto>(d => d.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    // F.I.R.S.T: kiểm chứng lỗi ổn định.
    // 3A — Arrange: cache miss + repo null. Act/Assert: ném 404 CommentNotFound.
    [Fact]
    public async Task CM03_GetByIdAsync_ShouldThrow404_WhenNotFound()
    {
        var id = Guid.NewGuid();
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.GetCommentByIdRouteReadAsync(id, null, It.IsAny<CancellationToken>())).ReturnsAsync((CommentDto?)null);
        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(x => x.GetJsonAsync<CommentDto>(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((CommentDto?)null);

        var sut = CreateSut(repo, cache);
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.GetByIdAsync(id));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.CommentNotFound, ex.ErrorCode);
    }

    // F.I.R.S.T: phủ đường list unpaged không filter content.
    // 3A — Arrange: repo trả 2 entity. Act: gọi GetCommentListAsync(unpaged=true). Assert: trả đủ 2 dòng và TotalCount đúng.
    [Fact]
    public async Task CM04_GetCommentListAsync_ShouldReturnUnpagedGlobal_WhenNoContentAndNoPost()
    {
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.LoadFlatUnpagedAsync(null, null, null, null, null, It.IsAny<CommentRouteListSort>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Comment>
            {
                new() { Id = Guid.NewGuid(), Content = "a", CreatedAt = DateTime.UtcNow, PostId = Guid.NewGuid(), UserId = Guid.NewGuid() },
                new() { Id = Guid.NewGuid(), Content = "b", CreatedAt = DateTime.UtcNow, PostId = Guid.NewGuid(), UserId = Guid.NewGuid() }
            });
        var cache = new Mock<IEntityResponseCache>();
        var sut = CreateSut(repo, cache);

        var result = await sut.GetCommentListAsync(null, null, true, 1, 10);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
        Assert.Null(result.TotalComments);
        Assert.Null(result.TotalNodes);
    }

    // F.I.R.S.T: kiểm tra biên search term.
    // 3A — Arrange: content trắng được xem như không search; cache trả trang. Act: gọi list. Assert: không ném và trả từ cache.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public async Task CM05_GetCommentListAsync_ShouldTreatWhitespaceAsNoSearch_AndUsePagedFlow(string content)
    {
        var repo = new Mock<ICommentRepository>(MockBehavior.Strict);
        var expected = new PagedResult<CommentDto> { Items = new List<CommentDto>(), Page = 1, PageSize = 10, TotalCount = 0 };
        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(x => x.GetJsonAsync<PagedResult<CommentDto>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var sut = CreateSut(repo, cache);

        var result = await sut.GetCommentListAsync(null, content, false, 1, 10);

        Assert.Same(expected, result);
    }

    // F.I.R.S.T: xác minh validation tạo mới theo thứ tự guard.
    // 3A — Arrange: PostExists=false. Act/Assert: CreateAsync ném PostNotFound và không gọi UserExists.
    [Fact]
    public async Task CM06_CreateAsync_ShouldThrow404_WhenPostNotFound()
    {
        var dto = new CreateCommentDto { Content = "x", PostId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.PostExistsAsync(dto.PostId)).ReturnsAsync(false);
        var sut = CreateSut(repo, new Mock<IEntityResponseCache>());

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.CreateAsync(dto));

        Assert.Equal(ApiErrorCodes.PostNotFound, ex.ErrorCode);
        repo.Verify(x => x.UserExistsAsync(It.IsAny<Guid>()), Times.Never);
    }

    // F.I.R.S.T: xác minh nhánh parent không hợp lệ.
    // 3A — Arrange: post/user có, parent không hợp lệ. Act/Assert: ném 400 CommentParentInvalid.
    [Fact]
    public async Task CM07_CreateAsync_ShouldThrow400_WhenParentInvalid()
    {
        var dto = new CreateCommentDto
        {
            Content = "x",
            PostId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ParentId = Guid.NewGuid()
        };
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.PostExistsAsync(dto.PostId)).ReturnsAsync(true);
        repo.Setup(x => x.UserExistsAsync(dto.UserId)).ReturnsAsync(true);
        repo.Setup(x => x.ParentExistsAsync(dto.ParentId!.Value, dto.PostId)).ReturnsAsync(false);
        var sut = CreateSut(repo, new Mock<IEntityResponseCache>());

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.CreateAsync(dto));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.CommentParentInvalid, ex.ErrorCode);
    }

    // F.I.R.S.T: đường thành công tạo mới.
    // 3A — Arrange: mọi guard pass. Act: CreateAsync. Assert: Add + SaveChanges được gọi đúng 1 lần.
    [Fact]
    public async Task CM08_CreateAsync_ShouldPersist_WhenValid()
    {
        var dto = new CreateCommentDto { Content = "ok", PostId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.PostExistsAsync(dto.PostId)).ReturnsAsync(true);
        repo.Setup(x => x.UserExistsAsync(dto.UserId)).ReturnsAsync(true);
        repo.Setup(x => x.AddAsync(It.IsAny<Comment>())).Returns(Task.CompletedTask);
        repo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);
        var sut = CreateSut(repo, new Mock<IEntityResponseCache>());

        var result = await sut.CreateAsync(dto);

        Assert.Equal("ok", result.Content);
        repo.Verify(x => x.AddAsync(It.IsAny<Comment>()), Times.Once);
        repo.Verify(x => x.SaveChangesAsync(), Times.Once);
    }

    // F.I.R.S.T: kiểm chứng quyền tác giả.
    // 3A — Arrange: comment có UserId khác currentUserId. Act/Assert: ném 403 NotResourceAuthor.
    [Fact]
    public async Task CM09_UpdateAsAuthorAsync_ShouldThrow403_WhenNotAuthor()
    {
        var id = Guid.NewGuid();
        var entity = new Comment { Id = id, Content = "old", PostId = Guid.NewGuid(), UserId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(entity);
        var sut = CreateSut(repo, new Mock<IEntityResponseCache>());

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateAsAuthorAsync(id, new UpdateCommentDto { Content = "new" }, Guid.NewGuid()));

        Assert.Equal(StatusCodes.Status403Forbidden, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.NotResourceAuthor, ex.ErrorCode);
    }

    // F.I.R.S.T: đường thành công update tác giả.
    // 3A — Arrange: đúng tác giả + post còn tồn tại. Act: UpdateAsAuthorAsync. Assert: content đổi, SaveChanges và Remove cache được gọi.
    [Fact]
    public async Task CM10_UpdateAsAuthorAsync_ShouldUpdateAndInvalidateCache_WhenValid()
    {
        var id = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var entity = new Comment { Id = id, Content = "old", PostId = Guid.NewGuid(), UserId = authorId, CreatedAt = DateTime.UtcNow };
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(entity);
        repo.Setup(x => x.PostExistsAsync(entity.PostId)).ReturnsAsync(true);
        repo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);
        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var sut = CreateSut(repo, cache);

        await sut.UpdateAsAuthorAsync(id, new UpdateCommentDto { Content = "new-value" }, authorId);

        Assert.Equal("new-value", entity.Content);
        repo.Verify(x => x.SaveChangesAsync(), Times.Once);
        cache.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // F.I.R.S.T: phủ đường xóa không tồn tại.
    // 3A — Arrange: GetByIdAsync trả null. Act/Assert: DeleteAsync ném 404.
    [Fact]
    public async Task CM11_DeleteAsync_ShouldThrow404_WhenCommentMissing()
    {
        var id = Guid.NewGuid();
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.GetByIdAsync(id)).ReturnsAsync((Comment?)null);
        var sut = CreateSut(repo, new Mock<IEntityResponseCache>());

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.DeleteAsync(id));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.CommentNotFound, ex.ErrorCode);
    }

    // F.I.R.S.T: kiểm chứng xóa theo cây con.
    // 3A — Arrange: root + child. Act: DeleteAsync(root). Assert: Remove được gọi cho cả root và child.
    [Fact]
    public async Task CM12_DeleteAsync_ShouldDeleteSubtree_WhenRootHasChildren()
    {
        var postId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var root = new Comment { Id = rootId, Content = "r", PostId = postId, UserId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var child = new Comment { Id = childId, Content = "c", PostId = postId, ParentId = rootId, UserId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };

        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.GetByIdAsync(rootId)).ReturnsAsync(root);
        repo.Setup(x => x.PostExistsAsync(postId)).ReturnsAsync(true);
        repo.Setup(x => x.LoadFlatUnpagedAsync(postId, null, null, null, null, CommentRouteListSort.ByCreatedAt, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Comment> { root, child });
        repo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);
        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(x => x.RemoveManyAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var sut = CreateSut(repo, cache);

        await sut.DeleteAsync(rootId);

        repo.Verify(x => x.Remove(It.Is<Comment>(c => c.Id == rootId)), Times.Once);
        repo.Verify(x => x.Remove(It.Is<Comment>(c => c.Id == childId)), Times.Once);
        repo.Verify(x => x.SaveChangesAsync(), Times.Once);
        cache.Verify(x => x.RemoveManyAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // F.I.R.S.T: bao phủ route [08] dữ liệu phẳng EF.
    // 3A — Arrange: repo trả entity có thứ tự ổn định. Act: GetFlatRoutePagedAsync. Assert: map DTO và metadata trang đúng.
    [Fact]
    public async Task CM13_GetFlatRoutePagedAsync_ShouldMapFromLoadFlat()
    {
        var postId = Guid.NewGuid();
        var entities = new List<Comment>
        {
            new() { Id = Guid.NewGuid(), Content = "a", CreatedAt = DateTime.UtcNow, PostId = postId, UserId = Guid.NewGuid() }
        };
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.PostExistsAsync(postId)).ReturnsAsync(true);
        // [08] GetFlatByPostIdPagedAsync: LoadFlatAsync [1][8][10][12] (danh sách phẳng).
        repo.Setup(x => x.LoadFlatAsync(postId, 1, 10, It.IsAny<CancellationToken>(), null, null, null, null, It.IsAny<CommentRouteListSort>()))
            .ReturnsAsync((entities, 1L));
        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(x => x.GetJsonAsync<PagedResult<CommentDto>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagedResult<CommentDto>?)null);
        cache.Setup(x => x.SetJsonAsync(It.IsAny<string>(), It.IsAny<PagedResult<CommentDto>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        cache.Setup(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var sut = CreateSut(repo, cache);

        var result = await sut.GetFlatRoutePagedAsync(postId, 1, 10);

        Assert.Single(result.Items);
        Assert.Equal("a", result.Items[0].Content);
        Assert.Equal(1L, result.TotalCount);
        Assert.Null(result.TotalComments);
        Assert.Null(result.TotalNodes);
    }

    // F.I.R.S.T: bao phủ route [11] build tree CTE ở service.
    // 3A — Arrange: raw rows root + child. Act: GetTreeCteRoutePagedAsync. Assert: child được gắn vào root và giữ Level.
    [Fact]
    public async Task CM14_GetTreeCteRoutePagedAsync_ShouldBuildTree_FromRawCteRows()
    {
        var postId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var rows = new List<CommentCteDto>
        {
            new() { Id = rootId, PostId = postId, UserId = Guid.NewGuid(), ParentId = null, Content = "root", Level = 0, CreatedAt = now },
            new() { Id = childId, PostId = postId, UserId = Guid.NewGuid(), ParentId = rootId, Content = "child", Level = 1, CreatedAt = now.AddSeconds(1) }
        };

        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.PostExistsAsync(postId)).ReturnsAsync(true);
        repo.Setup(x => x.CountCommentsMatchingRouteAsync(postId, null, It.IsAny<CancellationToken>(), null, null, null))
            .ReturnsAsync(2L);
        repo.Setup(x => x.LoadRawCteAsync(postId, It.IsAny<CancellationToken>(), null, null, null, null, It.IsAny<CommentRouteListSort>())).ReturnsAsync(rows);
        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(x => x.GetJsonAsync<PagedResult<CommentTreeCteDto>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagedResult<CommentTreeCteDto>?)null);
        cache.Setup(x => x.SetJsonAsync(It.IsAny<string>(), It.IsAny<PagedResult<CommentTreeCteDto>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = CreateSut(repo, cache);

        var result = await sut.GetTreeCteRoutePagedAsync(postId, 1, 10);

        Assert.Single(result.Items);
        Assert.Single(result.Items[0].Children);
        Assert.Equal(0, result.Items[0].Level);
        Assert.Equal(1, result.Items[0].Children[0].Level);
        Assert.Equal(1L, result.TotalCount);
        Assert.Equal(2L, result.TotalComments);
        Assert.Equal(1L, result.TotalNodes);
    }

    // F.I.R.S.T: bao phủ route [12] flatten tree flat ở service.
    // 3A — Arrange: roots + rawComments đủ cây 2 tầng. Act: GetTreeFlatFlattenRoutePagedAsync. Assert: preorder root→child và Level 0/1 giống pipeline CTE flatten.
    [Fact]
    public async Task CM15_GetTreeFlatFlattenRoutePagedAsync_ShouldFlattenPreorder_FromRawFlatData()
    {
        var postId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;
        var roots = new List<Comment>
        {
            new() { Id = rootId, PostId = postId, ParentId = null, Content = "root", UserId = Guid.NewGuid(), CreatedAt = t0 }
        };
        var rawComments = new List<Comment>
        {
            roots[0],
            new() { Id = childId, PostId = postId, ParentId = rootId, Content = "child", UserId = Guid.NewGuid(), CreatedAt = t0.AddSeconds(1) }
        };

        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.PostExistsAsync(postId)).ReturnsAsync(true);
        repo.Setup(x => x.CountCommentsMatchingRouteAsync(postId, null, It.IsAny<CancellationToken>(), null, null, null))
            .ReturnsAsync(2L);
        repo.Setup(x => x.CountCommentRootsMatchingRouteAsync(postId, null, It.IsAny<CancellationToken>(), null, null, null))
            .ReturnsAsync(1L);
        // BuildFlatTreesPagedCoreAsync: một trang gốc → LoadCommentsForSubtreesAsync → cây đầy đủ → flatten preorder.
        repo.Setup(x => x.GetCommentRootsRoutePagedAsync(postId, null, 1, 10, It.IsAny<CancellationToken>(), null, null, null, It.IsAny<CommentRouteListSort>()))
            .ReturnsAsync((new List<Comment> { roots[0] }, 1L));
        repo.Setup(x => x.LoadCommentsForSubtreesAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>(), It.IsAny<CommentRouteListSort>()))
            .ReturnsAsync(rawComments);
        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(x => x.GetJsonAsync<PagedResult<CommentFlattenFlatDto>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagedResult<CommentFlattenFlatDto>?)null);
        cache.Setup(x => x.SetJsonAsync(It.IsAny<string>(), It.IsAny<PagedResult<CommentFlattenFlatDto>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = CreateSut(repo, cache);

        var result = await sut.GetTreeFlatFlattenRoutePagedAsync(postId, 1, 10);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(rootId, result.Items[0].Id);
        Assert.Equal(childId, result.Items[1].Id);
        Assert.Equal(0, result.Items[0].Level);
        Assert.Equal(1, result.Items[1].Level);
        Assert.Equal(1L, result.TotalCount);
        Assert.Equal(2L, result.TotalComments);
        Assert.Equal(1L, result.TotalNodes);
    }

    // F.I.R.S.T: bao phủ route [13] flatten tree cte ở service.
    // 3A — Arrange: raw CTE root+child. Act: GetTreeCteFlattenRoutePagedAsync. Assert: danh sách phẳng giữ Level 0/1.
    [Fact]
    public async Task CM16_GetTreeCteFlattenRoutePagedAsync_ShouldFlattenTree_WithLevels()
    {
        var postId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var rows = new List<CommentCteDto>
        {
            new() { Id = rootId, Content = "root", PostId = postId, UserId = Guid.NewGuid(), ParentId = null, Level = 0, CreatedAt = now },
            new() { Id = childId, Content = "child", PostId = postId, UserId = Guid.NewGuid(), ParentId = rootId, Level = 1, CreatedAt = now.AddSeconds(1) }
        };

        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.PostExistsAsync(postId)).ReturnsAsync(true);
        repo.Setup(x => x.CountCommentsMatchingRouteAsync(postId, null, It.IsAny<CancellationToken>(), null, null, null))
            .ReturnsAsync(2L);
        repo.Setup(x => x.LoadRawCteAsync(postId, It.IsAny<CancellationToken>(), null, null, null, null, It.IsAny<CommentRouteListSort>())).ReturnsAsync(rows);
        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(x => x.GetJsonAsync<PagedResult<CommentFlattenCteDto>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagedResult<CommentFlattenCteDto>?)null);
        cache.Setup(x => x.SetJsonAsync(It.IsAny<string>(), It.IsAny<PagedResult<CommentFlattenCteDto>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = CreateSut(repo, cache);

        var result = await sut.GetTreeCteFlattenRoutePagedAsync(postId, 1, 10);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(0, result.Items[0].Level);
        Assert.Equal(1, result.Items[1].Level);
        Assert.Equal(1L, result.TotalCount);
        Assert.Equal(2L, result.TotalComments);
        Assert.Equal(1L, result.TotalNodes);
    }

    // F.I.R.S.T: bao phủ GET /api/posts/{id}/comments/tree qua service (GetAllCommentsForPost + BuildTreeCte).
    // 3A — Arrange: post tồn tại, repo trả 2 dòng CommentCteDto (gốc+con). Act: GetCommentsTreeForPostAsync. Assert: một gốc, một con, Level 0/1.
    [Fact]
    public async Task CM17_GetCommentsTreeForPostAsync_ShouldBuildCteTree_WhenCommentsExist()
    {
        var postId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var cteRows = new List<CommentCteDto>
        {
            new() { Id = rootId, Content = "r", PostId = postId, UserId = userId, ParentId = null, Level = 0, CreatedAt = now },
            new() { Id = childId, Content = "c", PostId = postId, UserId = userId, ParentId = rootId, Level = 1, CreatedAt = now.AddSeconds(1) }
        };
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.PostExistsAsync(postId)).ReturnsAsync(true);
        repo.Setup(x => x.GetAllCommentsForPost(postId, true, It.IsAny<CancellationToken>(), It.IsAny<CommentRouteListSort>())).ReturnsAsync(cteRows);
        var sut = CreateSut(repo, new Mock<IEntityResponseCache>());

        var result = await sut.GetCommentsTreeForPostAsync(postId);

        Assert.Single(result);
        Assert.Equal(rootId, result[0].Id);
        Assert.Equal(0, result[0].Level);
        Assert.Single(result[0].Children);
        Assert.Equal(childId, result[0].Children[0].Id);
        Assert.Equal(1, result[0].Children[0].Level);
    }

    // F.I.R.S.T: bao phủ GET /api/posts/{id}/comments/flat (danh sách phẳng từ GetAllCommentsForPost).
    // 3A — Arrange: giống CM17. Act: GetCommentsFlatForPostAsync. Assert: trả đúng 2 dòng Level 0 rồi 1.
    [Fact]
    public async Task CM18_GetCommentsFlatForPostAsync_ShouldReturnRawCteRows_WhenCommentsExist()
    {
        var postId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var cteRows = new List<CommentCteDto>
        {
            new() { Id = rootId, Content = "r", PostId = postId, UserId = userId, ParentId = null, Level = 0, CreatedAt = now },
            new() { Id = childId, Content = "c", PostId = postId, UserId = userId, ParentId = rootId, Level = 1, CreatedAt = now.AddSeconds(1) }
        };
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.PostExistsAsync(postId)).ReturnsAsync(true);
        repo.Setup(x => x.GetAllCommentsForPost(postId, true, It.IsAny<CancellationToken>(), It.IsAny<CommentRouteListSort>())).ReturnsAsync(cteRows);
        var sut = CreateSut(repo, new Mock<IEntityResponseCache>());

        var result = await sut.GetCommentsFlatForPostAsync(postId);

        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].Level);
        Assert.Equal(1, result[1].Level);
    }

    // F.I.R.S.T: bài không có comment — GetAllCommentsForPost trả rỗng, BuildTreeCte → rừng rỗng.
    // 3A — Arrange: post tồn tại, GetAllCommentsForPost rỗng. Act: GetCommentsTreeForPostAsync. Assert: rỗng; chỉ một lần gọi GetAllCommentsForPost.
    [Fact]
    public async Task CM19_GetCommentsTreeForPostAsync_ShouldReturnEmpty_WhenNoComments()
    {
        var postId = Guid.NewGuid();
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.PostExistsAsync(postId)).ReturnsAsync(true);
        repo.Setup(x => x.GetAllCommentsForPost(postId, true, It.IsAny<CancellationToken>(), It.IsAny<CommentRouteListSort>())).ReturnsAsync(new List<CommentCteDto>());
        var sut = CreateSut(repo, new Mock<IEntityResponseCache>());

        var result = await sut.GetCommentsTreeForPostAsync(postId);

        Assert.Empty(result);
        repo.Verify(x => x.GetAllCommentsForPost(postId, true, It.IsAny<CancellationToken>(), It.IsAny<CommentRouteListSort>()), Times.Once);
    }

    // F.I.R.S.T: includeReplies=false được truyền xuống repository.
    // 3A — Arrange: post tồn tại. Act: GetCommentsFlatForPostAsync(..., false). Assert: repo nhận includeReplies false.
    [Fact]
    public async Task CM21_GetCommentsFlatForPostAsync_ShouldPassIncludeRepliesFalse_ToRepository()
    {
        var postId = Guid.NewGuid();
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.PostExistsAsync(postId)).ReturnsAsync(true);
        repo.Setup(x => x.GetAllCommentsForPost(postId, false, It.IsAny<CancellationToken>(), It.IsAny<CommentRouteListSort>())).ReturnsAsync(new List<CommentCteDto>());
        var sut = CreateSut(repo, new Mock<IEntityResponseCache>());

        await sut.GetCommentsFlatForPostAsync(postId, includeReplies: false);

        repo.Verify(x => x.GetAllCommentsForPost(postId, false, It.IsAny<CancellationToken>(), It.IsAny<CommentRouteListSort>()), Times.Once);
    }

    // F.I.R.S.T: post không tồn tại — 404 trước khi đọc comment.
    // 3A — Arrange: PostExists false. Act: GetCommentsFlatForPostAsync. Assert: ApiException + không gọi GetAllCommentsForPost.
    [Fact]
    public async Task CM20_GetCommentsFlatForPostAsync_ShouldThrow_WhenPostMissing()
    {
        var postId = Guid.NewGuid();
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.PostExistsAsync(postId)).ReturnsAsync(false);
        var sut = CreateSut(repo, new Mock<IEntityResponseCache>());

        await Assert.ThrowsAsync<ApiException>(() => sut.GetCommentsFlatForPostAsync(postId));

        repo.Verify(x => x.GetAllCommentsForPost(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<CommentRouteListSort>()), Times.Never);
    }

    // F.I.R.S.T: negative — post không tồn tại trên route tree.
    // 3A — Arrange: PostExists false. Act: GetCommentsTreeForPostAsync. Assert: ApiException; không gọi GetAllCommentsForPost.
    [Fact]
    public async Task CM22_GetCommentsTreeForPostAsync_ShouldThrow_WhenPostMissing()
    {
        var postId = Guid.NewGuid();
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.PostExistsAsync(postId)).ReturnsAsync(false);
        var sut = CreateSut(repo, new Mock<IEntityResponseCache>());

        await Assert.ThrowsAsync<ApiException>(() => sut.GetCommentsTreeForPostAsync(postId));

        repo.Verify(x => x.GetAllCommentsForPost(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<CommentRouteListSort>()), Times.Never);
    }

    // F.I.R.S.T: happy path biên — bài tồn tại nhưng không có comment; flat trả rỗng.
    // 3A — Arrange: post ok, GetAllCommentsForPost rỗng. Act: GetCommentsFlatForPostAsync. Assert: Count 0; repo gọi đúng một lần.
    [Fact]
    public async Task CM23_GetCommentsFlatForPostAsync_ShouldReturnEmpty_WhenPostHasNoComments()
    {
        var postId = Guid.NewGuid();
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.PostExistsAsync(postId)).ReturnsAsync(true);
        repo.Setup(x => x.GetAllCommentsForPost(postId, true, It.IsAny<CancellationToken>(), It.IsAny<CommentRouteListSort>())).ReturnsAsync(new List<CommentCteDto>());
        var sut = CreateSut(repo, new Mock<IEntityResponseCache>());

        var result = await sut.GetCommentsFlatForPostAsync(postId);

        Assert.Empty(result);
        repo.Verify(x => x.GetAllCommentsForPost(postId, true, It.IsAny<CancellationToken>(), It.IsAny<CommentRouteListSort>()), Times.Once);
    }

    // F.I.R.S.T: includeReplies=false trên tree — truyền xuống repository.
    // 3A — Arrange: post tồn tại. Act: GetCommentsTreeForPostAsync(..., false). Assert: GetAllCommentsForPost(postId, false, ...).
    [Fact]
    public async Task CM24_GetCommentsTreeForPostAsync_ShouldPassIncludeRepliesFalse_ToRepository()
    {
        var postId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var flatRows = new List<CommentCteDto>
        {
            new() { Id = rootId, Content = "only-root", PostId = postId, UserId = userId, ParentId = null, Level = 0, CreatedAt = now }
        };
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.PostExistsAsync(postId)).ReturnsAsync(true);
        repo.Setup(x => x.GetAllCommentsForPost(postId, false, It.IsAny<CancellationToken>(), It.IsAny<CommentRouteListSort>())).ReturnsAsync(flatRows);
        var sut = CreateSut(repo, new Mock<IEntityResponseCache>());

        var result = await sut.GetCommentsTreeForPostAsync(postId, includeReplies: false);

        Assert.Single(result);
        Assert.Equal(rootId, result[0].Id);
        repo.Verify(x => x.GetAllCommentsForPost(postId, false, It.IsAny<CancellationToken>(), It.IsAny<CommentRouteListSort>()), Times.Once);
    }

    // F.I.R.S.T: happy path — hai gốc cùng bài (CTE phẳng hai dòng Level 0); BuildTreeCte trả hai nút gốc.
    // 3A — Arrange: hai CommentCteDto ParentId null cùng PostId. Act: GetCommentsTreeForPostAsync. Assert: hai phần tử cấp một, không con.
    [Fact]
    public async Task CM25_GetCommentsTreeForPostAsync_ShouldReturnTwoRoots_WhenFlatHasTwoAnchors()
    {
        var postId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();
        var flatRows = new List<CommentCteDto>
        {
            new() { Id = r1, Content = "t1", PostId = postId, UserId = userId, ParentId = null, Level = 0, CreatedAt = now },
            new() { Id = r2, Content = "t2", PostId = postId, UserId = userId, ParentId = null, Level = 0, CreatedAt = now.AddSeconds(1) }
        };
        var repo = new Mock<ICommentRepository>();
        repo.Setup(x => x.PostExistsAsync(postId)).ReturnsAsync(true);
        repo.Setup(x => x.GetAllCommentsForPost(postId, true, It.IsAny<CancellationToken>(), It.IsAny<CommentRouteListSort>())).ReturnsAsync(flatRows);
        var sut = CreateSut(repo, new Mock<IEntityResponseCache>());

        var result = (await sut.GetCommentsTreeForPostAsync(postId)).OrderBy(x => x.Id).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, n => Assert.Empty(n.Children));
        Assert.Contains(result, n => n.Id == r1);
        Assert.Contains(result, n => n.Id == r2);
    }
}
