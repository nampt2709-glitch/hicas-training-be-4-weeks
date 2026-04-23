using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Entities;
using CommentAPI.Interfaces;
using CommentAPI.Services;
using Microsoft.AspNetCore.Http;
using Moq;

namespace CommentAPI.Tests;

// Nhóm unit test CommentService: mock repository + cache + AutoMapper thật (MappingProfile).
public class CommentServiceTests
{
    // F.I.R.S.T: nhanh, độc lập, lặp lại ổn định, tự kiểm chứng, kịp thời.
    // 3A — Arrange: cache trả sẵn DTO. Act: gọi GetByIdAsync. Assert: không gọi repository đọc.
    [Fact]
    public async Task CM01_GetByIdAsync_ShouldReturnFromCache_WhenCacheHit()
    {
        var id = Guid.NewGuid();
        var cached = new CommentDto
        {
            Id = id,
            Content = "cached",
            CreatedAt = DateTime.UtcNow,
            PostId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ParentId = null
        };

        var repo = new Mock<ICommentRepository>(MockBehavior.Strict);
        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.GetJsonAsync<CommentDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), cache.Object);
        var result = await sut.GetByIdAsync(id);

        Assert.Same(cached, result);
        repo.Verify(r => r.GetByIdForReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // F.I.R.S.T: độc lập, không phụ thuộc thứ tự chạy test khác.
    // 3A — Arrange: cache miss, repo trả DTO. Act: GetByIdAsync. Assert: SetJsonAsync được gọi đúng kiểu.
    [Fact]
    public async Task CM02_GetByIdAsync_ShouldQueryRepository_AndPopulateCache_WhenCacheMiss()
    {
        var id = Guid.NewGuid();
        var dto = new CommentDto
        {
            Id = id,
            Content = "db",
            CreatedAt = DateTime.UtcNow,
            PostId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ParentId = null
        };

        var repo = new Mock<ICommentRepository>();
        repo.Setup(r => r.GetByIdForReadAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.GetJsonAsync<CommentDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CommentDto?)null);
        cache.Setup(c => c.SetJsonAsync(It.IsAny<string>(), It.IsAny<CommentDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), cache.Object);
        var result = await sut.GetByIdAsync(id);

        Assert.Equal(id, result.Id);
        Assert.Equal("db", result.Content);
        cache.Verify(
            c => c.SetJsonAsync(It.IsAny<string>(), It.Is<CommentDto>(x => x.Id == id), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // F.I.R.S.T: tự kiểm chứng qua ApiException cố định.
    // 3A — Arrange: miss + repo null. Act/Assert: ném 404 đúng mã lỗi.
    [Fact]
    public async Task CM03_GetByIdAsync_ShouldThrow404_WhenNotFound()
    {
        var id = Guid.NewGuid();
        var repo = new Mock<ICommentRepository>();
        repo.Setup(r => r.GetByIdForReadAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((CommentDto?)null);

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.GetJsonAsync<CommentDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CommentDto?)null);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), cache.Object);
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.GetByIdAsync(id));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.CommentNotFound, ex.ErrorCode);
    }

    // F.I.R.S.T: lặp lại ổn định với Theory biên chuỗi rỗng.
    // 3A — Arrange: term null hoặc chỉ khoảng trắng. Act: SearchByContentPagedAsync. Assert: 400 SEARCH_TERM_REQUIRED.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task CM04_SearchByContentPagedAsync_ShouldThrow400_WhenTermMissing(string? raw)
    {
        var repo = new Mock<ICommentRepository>(MockBehavior.Strict);
        var cache = new Mock<IEntityResponseCache>(MockBehavior.Strict);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), cache.Object);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.SearchByContentPagedAsync(raw, page: 1, pageSize: 10, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.SearchTermRequired, ex.ErrorCode);
    }

    // F.I.R.S.T: một ký tự không trắng là term hợp lệ (biên dưới độ dài).
    // 3A — Arrange: repo trả rỗng, cache miss. Act: search "a". Assert: không ném, TotalCount = 0.
    [Fact]
    public async Task CM05_SearchByContentPagedAsync_ShouldSucceed_WhenSingleCharTerm()
    {
        var repo = new Mock<ICommentRepository>();
        repo.Setup(r => r.SearchByContentPagedAsync("a", 1, 10, It.IsAny<CancellationToken>(), null, null))
            .ReturnsAsync((new List<Comment>(), 0L));

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.GetJsonAsync<PagedResult<CommentDto>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagedResult<CommentDto>?)null);
        cache.Setup(c => c.SetJsonAsync(It.IsAny<string>(), It.IsAny<PagedResult<CommentDto>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), cache.Object);
        var page = await sut.SearchByContentPagedAsync("a", 1, 10);

        Assert.Empty(page.Items);
        Assert.Equal(0L, page.TotalCount);
    }

    // F.I.R.S.T: độc lập với DB thật.
    // 3A — Arrange: PostExists false. Act: CreateAsync. Assert: PostNotFound.
    [Fact]
    public async Task CM06_CreateAsync_ShouldThrow404_WhenPostMissing()
    {
        var dto = new CreateCommentDto
        {
            Content = "x",
            PostId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ParentId = null
        };

        var repo = new Mock<ICommentRepository>();
        repo.Setup(r => r.PostExistsAsync(dto.PostId)).ReturnsAsync(false);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.CreateAsync(dto));

        Assert.Equal(ApiErrorCodes.PostNotFound, ex.ErrorCode);
        repo.Verify(r => r.UserExistsAsync(It.IsAny<Guid>()), Times.Never);
    }

    // F.I.R.S.T: kiểm tra thứ tự guard (post ok, user fail).
    // 3A — Arrange: post tồn tại, user không. Act: CreateAsync. Assert: UserNotFound.
    [Fact]
    public async Task CM07_CreateAsync_ShouldThrow404_WhenUserMissing()
    {
        var dto = new CreateCommentDto
        {
            Content = "x",
            PostId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ParentId = null
        };

        var repo = new Mock<ICommentRepository>();
        repo.Setup(r => r.PostExistsAsync(dto.PostId)).ReturnsAsync(true);
        repo.Setup(r => r.UserExistsAsync(dto.UserId)).ReturnsAsync(false);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.CreateAsync(dto));

        Assert.Equal(ApiErrorCodes.UserNotFound, ex.ErrorCode);
    }

    // F.I.R.S.T: biên ParentId có giá trị nhưng cha không tồn tại trong post.
    // 3A — Arrange: post+user ok, ParentExists false. Act: CreateAsync. Assert: CommentParentInvalid.
    [Fact]
    public async Task CM08_CreateAsync_ShouldThrow400_WhenParentInvalid()
    {
        var parentId = Guid.NewGuid();
        var dto = new CreateCommentDto
        {
            Content = "x",
            PostId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ParentId = parentId
        };

        var repo = new Mock<ICommentRepository>();
        repo.Setup(r => r.PostExistsAsync(dto.PostId)).ReturnsAsync(true);
        repo.Setup(r => r.UserExistsAsync(dto.UserId)).ReturnsAsync(true);
        repo.Setup(r => r.ParentExistsAsync(parentId, dto.PostId)).ReturnsAsync(false);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.CreateAsync(dto));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.CommentParentInvalid, ex.ErrorCode);
    }

    // F.I.R.S.T: mapper + repository tương tác được kiểm chứng bằng AddAsync/SaveChanges.
    // 3A — Arrange: đủ điều kiện. Act: CreateAsync. Assert: Id không rỗng, nội dung khớp; nếu kỳ vọng Id sai cố ý thì test sẽ fail.
    [Fact]
    public async Task CM09_CreateAsync_ShouldPersist_AndReturnMappedDto_WhenValid()
    {
        var dto = new CreateCommentDto
        {
            Content = "hello",
            PostId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ParentId = null
        };

        Comment? captured = null;
        var repo = new Mock<ICommentRepository>();
        repo.Setup(r => r.PostExistsAsync(dto.PostId)).ReturnsAsync(true);
        repo.Setup(r => r.UserExistsAsync(dto.UserId)).ReturnsAsync(true);
        repo.Setup(r => r.AddAsync(It.IsAny<Comment>())).Callback<Comment>(c => captured = c).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var result = await sut.CreateAsync(dto);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("hello", result.Content);
        Assert.Equal(dto.PostId, result.PostId);
        Assert.Equal(dto.UserId, result.UserId);
        Assert.NotNull(captured);
        Assert.Equal(result.Id, captured!.Id);
        // Nếu cố ý Assert.Equal(Guid.Empty, result.Id) thì test fail — chứng tỏ assert chặt với lỗi gán Id.
        repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    // F.I.R.S.T: không cần DB.
    // 3A — Arrange: GetById null. Act: UpdateAsAuthorAsync. Assert: CommentNotFound.
    [Fact]
    public async Task CM10_UpdateAsAuthorAsync_ShouldThrow404_WhenCommentMissing()
    {
        var id = Guid.NewGuid();
        var repo = new Mock<ICommentRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Comment?)null);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.UpdateAsAuthorAsync(id, new UpdateCommentDto { Content = "z" }, Guid.NewGuid()));

        Assert.Equal(ApiErrorCodes.CommentNotFound, ex.ErrorCode);
    }

    // F.I.R.S.T: kiểm tra quyền tác giả.
    // 3A — Arrange: comment của user A, caller là B. Act: update. Assert: 403 NotResourceAuthor.
    [Fact]
    public async Task CM11_UpdateAsAuthorAsync_ShouldThrow403_WhenNotAuthor()
    {
        var author = Guid.NewGuid();
        var other = Guid.NewGuid();
        var id = Guid.NewGuid();
        var entity = new Comment
        {
            Id = id,
            Content = "old",
            PostId = Guid.NewGuid(),
            UserId = author,
            ParentId = null
        };

        var repo = new Mock<ICommentRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.UpdateAsAuthorAsync(id, new UpdateCommentDto { Content = "new" }, other));

        Assert.Equal(StatusCodes.Status403Forbidden, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.NotResourceAuthor, ex.ErrorCode);
    }

    // F.I.R.S.T: đường vui (happy path) cập nhật.
    // 3A — Arrange: đúng tác giả, post còn. Act: UpdateAsAuthorAsync. Assert: nội dung đổi, cache remove.
    [Fact]
    public async Task CM12_UpdateAsAuthorAsync_ShouldUpdate_WhenAuthorAndPostExists()
    {
        var author = Guid.NewGuid();
        var id = Guid.NewGuid();
        var entity = new Comment
        {
            Id = id,
            Content = "old",
            PostId = Guid.NewGuid(),
            UserId = author,
            ParentId = null
        };

        var repo = new Mock<ICommentRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);
        repo.Setup(r => r.PostExistsAsync(entity.PostId)).ReturnsAsync(true);
        repo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), cache.Object);
        await sut.UpdateAsAuthorAsync(id, new UpdateCommentDto { Content = "new" }, author);

        Assert.Equal("new", entity.Content);
        cache.Verify(c => c.RemoveAsync(It.Is<string>(k => k.Contains(id.ToString("N"), StringComparison.OrdinalIgnoreCase)), It.IsAny<CancellationToken>()), Times.Once);
    }

    // F.I.R.S.T: xóa an toàn khi không có bản ghi.
    // 3A — Arrange: GetById null. Act: DeleteAsync. Assert: 404.
    [Fact]
    public async Task CM13_DeleteAsync_ShouldThrow404_WhenCommentMissing()
    {
        var id = Guid.NewGuid();
        var repo = new Mock<ICommentRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Comment?)null);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.DeleteAsync(id));

        Assert.Equal(ApiErrorCodes.CommentNotFound, ex.ErrorCode);
    }

    // F.I.R.S.T: BFS subtree + RemoveMany cache.
    // 3A — Arrange: gốc + 1 con. Act: DeleteAsync. Assert: Remove hai entity, RemoveManyAsync chứa 2 khóa.
    [Fact]
    public async Task CM14_DeleteAsync_ShouldRemoveDescendants_AndInvalidateCacheKeys()
    {
        var postId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var root = new Comment { Id = rootId, Content = "r", PostId = postId, UserId = Guid.NewGuid(), ParentId = null };
        var child = new Comment { Id = childId, Content = "c", PostId = postId, UserId = Guid.NewGuid(), ParentId = rootId };

        var repo = new Mock<ICommentRepository>();
        repo.Setup(r => r.GetByIdAsync(rootId)).ReturnsAsync(root);
        repo.Setup(r => r.PostExistsAsync(postId)).ReturnsAsync(true);
        repo.Setup(r => r.GetByPostIdAsync(postId)).ReturnsAsync(new List<Comment> { root, child });
        repo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var cache = new Mock<IEntityResponseCache>();
        List<string>? removedKeys = null;
        cache.Setup(c => c.RemoveManyAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, CancellationToken>((keys, _) => removedKeys = keys.ToList())
            .Returns(Task.CompletedTask);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), cache.Object);
        await sut.DeleteAsync(rootId);

        repo.Verify(r => r.Remove(It.Is<Comment>(c => c.Id == rootId)), Times.Once);
        repo.Verify(r => r.Remove(It.Is<Comment>(c => c.Id == childId)), Times.Once);
        Assert.NotNull(removedKeys);
        Assert.Equal(2, removedKeys!.Count);
        Assert.Contains(removedKeys, k => k.Contains(rootId.ToString("N"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(removedKeys, k => k.Contains(childId.ToString("N"), StringComparison.OrdinalIgnoreCase));
        cache.Verify(c => c.RemoveManyAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // F.I.R.S.T: ghép post + comment.
    // 3A — Arrange: post tồn tại, repo trả null cho cặp id. Act: GetByIdInPostAsync. Assert: CommentNotFound (không phải PostNotFound nếu EnsurePost trước).
    [Fact]
    public async Task CM15_GetByIdInPostAsync_ShouldThrow404_WhenCommentNotInPost()
    {
        var postId = Guid.NewGuid();
        var commentId = Guid.NewGuid();

        var repo = new Mock<ICommentRepository>();
        repo.Setup(r => r.PostExistsAsync(postId)).ReturnsAsync(true);
        repo.Setup(r => r.GetByIdForReadInPostAsync(postId, commentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CommentDto?)null);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.GetByIdInPostAsync(postId, commentId));

        Assert.Equal(ApiErrorCodes.CommentNotFound, ex.ErrorCode);
    }

    // F.I.R.S.T: phân trang toàn cục với cache hit.
    // 3A — Arrange: cache có PagedResult. Act: GetAllPagedAsync. Assert: không gọi repository.
    [Fact]
    public async Task CM16_GetAllPagedAsync_ShouldReturnCache_WithoutRepository_WhenHit()
    {
        var cached = new PagedResult<CommentDto>
        {
            Items = new List<CommentDto>(),
            Page = 1,
            PageSize = 20,
            TotalCount = 0
        };

        var repo = new Mock<ICommentRepository>(MockBehavior.Strict);
        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.GetJsonAsync<PagedResult<CommentDto>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), cache.Object);
        var result = await sut.GetAllPagedAsync(1, 20);

        Assert.Same(cached, result);
    }

    // F.I.R.S.T: Search trong post — post không tồn tại.
    // 3A — Arrange: PostExists false. Act: SearchByContentInPostPagedAsync với term hợp lệ. Assert: PostNotFound trước khi search.
    [Fact]
    public async Task CM17_SearchByContentInPostPagedAsync_ShouldThrow404_WhenPostMissing()
    {
        var postId = Guid.NewGuid();
        var repo = new Mock<ICommentRepository>();
        repo.Setup(r => r.PostExistsAsync(postId)).ReturnsAsync(false);

        var sut = new CommentService(repo.Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.SearchByContentInPostPagedAsync(postId, "ok", 1, 10));

        Assert.Equal(ApiErrorCodes.PostNotFound, ex.ErrorCode);
    }
}
