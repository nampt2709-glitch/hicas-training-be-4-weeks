using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Entities;
using CommentAPI.Interfaces;
using CommentAPI.Services;
using Microsoft.AspNetCore.Http;
using Moq;

namespace CommentAPI.Tests;

// Unit test PostService: IPostRepository, IUserRepository, cache, mapper.
public class PostServiceTests
{
    // F.I.R.S.T: độc lập, không DB.
    // 3A — Arrange: cache hit. Act: GetPagedAsync. Assert: không gọi repository.
    [Fact]
    public async Task PS01_GetPagedAsync_ShouldReturnFromCache_WhenHit()
    {
        var cached = new PagedResult<PostDto>
        {
            Items = new List<PostDto>(),
            Page = 1,
            PageSize = 10,
            TotalCount = 0
        };

        var repo = new Mock<IPostRepository>(MockBehavior.Strict);
        var userRepo = new Mock<IUserRepository>(MockBehavior.Strict);
        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.GetJsonAsync<PagedResult<PostDto>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var sut = new PostService(repo.Object, userRepo.Object, TestMapperFactory.CreateMapper(), cache.Object);
        var r = await sut.GetPagedAsync(1, 10);

        Assert.Same(cached, r);
    }

    // F.I.R.S.T: đường miss ghi cache.
    // 3A — Arrange: repo trả trang PostDto. Act: GetPagedAsync. Assert: SetJsonAsync một lần.
    [Fact]
    public async Task PS02_GetPagedAsync_ShouldQueryRepository_WhenCacheMiss()
    {
        var dto = new PostDto { Id = Guid.NewGuid(), Title = "t", Content = "c", CreatedAt = DateTime.UtcNow, UserId = Guid.NewGuid() };
        var repo = new Mock<IPostRepository>();
        repo.Setup(r => r.GetPagedAsync(2, 5, It.IsAny<CancellationToken>())).ReturnsAsync((new List<PostDto> { dto }, 1L));

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.GetJsonAsync<PagedResult<PostDto>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagedResult<PostDto>?)null);
        cache.Setup(c => c.SetJsonAsync(It.IsAny<string>(), It.IsAny<PagedResult<PostDto>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PostService(repo.Object, Mock.Of<IUserRepository>(), TestMapperFactory.CreateMapper(), cache.Object);
        var r = await sut.GetPagedAsync(2, 5);

        Assert.Single(r.Items);
        Assert.Equal(1L, r.TotalCount);
        cache.Verify(c => c.SetJsonAsync(It.IsAny<string>(), It.IsAny<PagedResult<PostDto>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // F.I.R.S.T: biên term rỗng giống Comment/User search.
    // 3A — Arrange: title null/blank. Act: SearchByTitlePagedAsync. Assert: SearchTermRequired.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task PS03_SearchByTitlePagedAsync_ShouldThrow400_WhenTermMissing(string? title)
    {
        var sut = new PostService(
            Mock.Of<IPostRepository>(MockBehavior.Strict),
            Mock.Of<IUserRepository>(MockBehavior.Strict),
            TestMapperFactory.CreateMapper(),
            Mock.Of<IEntityResponseCache>(MockBehavior.Strict));

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.SearchByTitlePagedAsync(title, 1, 10, CancellationToken.None));

        Assert.Equal(ApiErrorCodes.SearchTermRequired, ex.ErrorCode);
    }

    // F.I.R.S.T: term một ký tự hợp lệ.
    // 3A — Arrange: repo trả rỗng. Act: search "x". Assert: TotalCount 0.
    [Fact]
    public async Task PS04_SearchByTitlePagedAsync_ShouldSucceed_WhenSingleCharTerm()
    {
        var repo = new Mock<IPostRepository>();
        repo.Setup(r => r.SearchByTitlePagedAsync("x", 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PostDto>(), 0L));

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.GetJsonAsync<PagedResult<PostDto>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagedResult<PostDto>?)null);
        cache.Setup(c => c.SetJsonAsync(It.IsAny<string>(), It.IsAny<PagedResult<PostDto>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PostService(repo.Object, Mock.Of<IUserRepository>(), TestMapperFactory.CreateMapper(), cache.Object);
        var r = await sut.SearchByTitlePagedAsync("x", 1, 20);

        Assert.Empty(r.Items);
        Assert.Equal(0L, r.TotalCount);
    }

    // F.I.R.S.T: cache-aside đọc chi tiết.
    // 3A — Arrange: cache có PostDto. Act: GetByIdAsync. Assert: không gọi repository.
    [Fact]
    public async Task PS05_GetByIdAsync_ShouldReturnFromCache_WhenHit()
    {
        var id = Guid.NewGuid();
        var cached = new PostDto { Id = id, Title = "a", Content = "b", CreatedAt = DateTime.UtcNow, UserId = Guid.NewGuid() };

        var repo = new Mock<IPostRepository>(MockBehavior.Strict);
        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.GetJsonAsync<PostDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var sut = new PostService(repo.Object, Mock.Of<IUserRepository>(), TestMapperFactory.CreateMapper(), cache.Object);
        var r = await sut.GetByIdAsync(id);

        Assert.Same(cached, r);
    }

    // F.I.R.S.T: 404 thống nhất API.
    // 3A — Arrange: miss + repo null. Act: GetByIdAsync. Assert: PostNotFound.
    [Fact]
    public async Task PS06_GetByIdAsync_ShouldThrow404_WhenMissing()
    {
        var id = Guid.NewGuid();
        var repo = new Mock<IPostRepository>();
        repo.Setup(r => r.GetByIdForReadAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((PostDto?)null);

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.GetJsonAsync<PostDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PostDto?)null);

        var sut = new PostService(repo.Object, Mock.Of<IUserRepository>(), TestMapperFactory.CreateMapper(), cache.Object);
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.GetByIdAsync(id));

        Assert.Equal(ApiErrorCodes.PostNotFound, ex.ErrorCode);
    }

    // F.I.R.S.T: FK user.
    // 3A — Arrange: user không tồn tại. Act: CreateAsync. Assert: UserNotFound, không AddAsync.
    [Fact]
    public async Task PS07_CreateAsync_ShouldThrow404_WhenUserMissing()
    {
        var uid = Guid.NewGuid();
        var dto = new CreatePostDto { Title = "t", Content = "c", UserId = uid };

        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(u => u.ExistsAsync(uid)).ReturnsAsync(false);

        var postRepo = new Mock<IPostRepository>(MockBehavior.Strict);

        var sut = new PostService(postRepo.Object, userRepo.Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.CreateAsync(dto));

        Assert.Equal(ApiErrorCodes.UserNotFound, ex.ErrorCode);
    }

    // F.I.R.S.T: happy path tạo bài.
    // 3A — Arrange: user ok. Act: CreateAsync. Assert: Id không rỗng; sai kỳ vọng Title sẽ fail.
    [Fact]
    public async Task PS08_CreateAsync_ShouldPersist_WhenUserExists()
    {
        var uid = Guid.NewGuid();
        var dto = new CreatePostDto { Title = "tiêu đề", Content = "nội dung", UserId = uid };

        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(u => u.ExistsAsync(uid)).ReturnsAsync(true);

        var postRepo = new Mock<IPostRepository>();
        postRepo.Setup(p => p.AddAsync(It.IsAny<Post>())).Returns(Task.CompletedTask);
        postRepo.Setup(p => p.SaveChangesAsync()).Returns(Task.CompletedTask);

        var sut = new PostService(postRepo.Object, userRepo.Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var result = await sut.CreateAsync(dto);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("tiêu đề", result.Title);
        Assert.NotEqual("sai cố ý", result.Title);
        postRepo.Verify(p => p.SaveChangesAsync(), Times.Once);
    }

    // F.I.R.S.T: quyền tác giả.
    // 3A — Arrange: entity null. Act: UpdateAsAuthorAsync. Assert: PostNotFound.
    [Fact]
    public async Task PS09_UpdateAsAuthorAsync_ShouldThrow404_WhenPostMissing()
    {
        var id = Guid.NewGuid();
        var repo = new Mock<IPostRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Post?)null);

        var sut = new PostService(repo.Object, Mock.Of<IUserRepository>(), TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.UpdateAsAuthorAsync(id, new UpdatePostDto { Title = "a", Content = "b" }, Guid.NewGuid()));

        Assert.Equal(ApiErrorCodes.PostNotFound, ex.ErrorCode);
    }

    // F.I.R.S.T: không phải chủ bài.
    // 3A — Arrange: UserId entity khác currentUser. Act: update. Assert: 403.
    [Fact]
    public async Task PS10_UpdateAsAuthorAsync_ShouldThrow403_WhenNotOwner()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var id = Guid.NewGuid();
        var entity = new Post { Id = id, Title = "o", Content = "o", UserId = owner, CreatedAt = DateTime.UtcNow };

        var repo = new Mock<IPostRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);

        var sut = new PostService(repo.Object, Mock.Of<IUserRepository>(), TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.UpdateAsAuthorAsync(id, new UpdatePostDto { Title = "n", Content = "n" }, other));

        Assert.Equal(StatusCodes.Status403Forbidden, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.NotResourceAuthor, ex.ErrorCode);
    }

    // F.I.R.S.T: cập nhật thành công + vô hiệu cache.
    // 3A — Arrange: đúng chủ. Act: UpdateAsAuthorAsync. Assert: Title đổi, RemoveAsync cache.
    [Fact]
    public async Task PS11_UpdateAsAuthorAsync_ShouldUpdate_AndInvalidateCache_WhenOwner()
    {
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        var entity = new Post { Id = id, Title = "old", Content = "old", UserId = owner, CreatedAt = DateTime.UtcNow };

        var repo = new Mock<IPostRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);
        repo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = new PostService(repo.Object, Mock.Of<IUserRepository>(), TestMapperFactory.CreateMapper(), cache.Object);
        await sut.UpdateAsAuthorAsync(id, new UpdatePostDto { Title = "new", Content = "new" }, owner);

        Assert.Equal("new", entity.Title);
        cache.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // F.I.R.S.T: admin cập nhật.
    // 3A — Arrange: post không tồn tại. Act: UpdateAsAdminAsync. Assert: 404.
    [Fact]
    public async Task PS12_UpdateAsAdminAsync_ShouldThrow404_WhenPostMissing()
    {
        var id = Guid.NewGuid();
        var repo = new Mock<IPostRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Post?)null);

        var sut = new PostService(repo.Object, Mock.Of<IUserRepository>(), TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.UpdateAsAdminAsync(id, new AdminUpdatePostDto { Title = "a", Content = "b", UserId = null }));

        Assert.Equal(ApiErrorCodes.PostNotFound, ex.ErrorCode);
    }

    // F.I.R.S.T: đổi chủ bài khi UserId có giá trị phải tồn tại.
    // 3A — Arrange: UserId mới không Exists. Act: UpdateAsAdminAsync. Assert: UserNotFound.
    [Fact]
    public async Task PS13_UpdateAsAdminAsync_ShouldThrow404_WhenNewOwnerMissing()
    {
        var id = Guid.NewGuid();
        var newOwner = Guid.NewGuid();
        var entity = new Post { Id = id, Title = "t", Content = "c", UserId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };

        var repo = new Mock<IPostRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.ExistsAsync(newOwner)).ReturnsAsync(false);

        var sut = new PostService(repo.Object, users.Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.UpdateAsAdminAsync(id, new AdminUpdatePostDto { Title = "t", Content = "c", UserId = newOwner }));

        Assert.Equal(ApiErrorCodes.UserNotFound, ex.ErrorCode);
    }

    // F.I.R.S.T: UserId null giữ chủ cũ.
    // 3A — Arrange: post tồn tại, DTO.UserId null. Act: UpdateAsAdminAsync. Assert: UserId entity không đổi, SaveChanges.
    [Fact]
    public async Task PS14_UpdateAsAdminAsync_ShouldKeepOwner_WhenUserIdNotSent()
    {
        var id = Guid.NewGuid();
        var originalOwner = Guid.NewGuid();
        var entity = new Post { Id = id, Title = "t", Content = "c", UserId = originalOwner, CreatedAt = DateTime.UtcNow };

        var repo = new Mock<IPostRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);
        repo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var users = new Mock<IUserRepository>(MockBehavior.Strict);

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = new PostService(repo.Object, users.Object, TestMapperFactory.CreateMapper(), cache.Object);
        await sut.UpdateAsAdminAsync(id, new AdminUpdatePostDto { Title = "nt", Content = "nc", UserId = null });

        Assert.Equal(originalOwner, entity.UserId);
        Assert.Equal("nt", entity.Title);
    }

    // F.I.R.S.T: xóa.
    // 3A — Arrange: không có post. Act: DeleteAsync. Assert: PostNotFound.
    [Fact]
    public async Task PS15_DeleteAsync_ShouldThrow404_WhenMissing()
    {
        var id = Guid.NewGuid();
        var repo = new Mock<IPostRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Post?)null);

        var sut = new PostService(repo.Object, Mock.Of<IUserRepository>(), TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.DeleteAsync(id));

        Assert.Equal(ApiErrorCodes.PostNotFound, ex.ErrorCode);
    }

    // F.I.R.S.T: xóa + cache trước Remove.
    // 3A — Arrange: entity tồn tại. Act: DeleteAsync. Assert: Remove + SaveChanges, Remove cache.
    [Fact]
    public async Task PS16_DeleteAsync_ShouldRemove_WhenExists()
    {
        var id = Guid.NewGuid();
        var entity = new Post { Id = id, Title = "t", Content = "c", UserId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };

        var repo = new Mock<IPostRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);
        repo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = new PostService(repo.Object, Mock.Of<IUserRepository>(), TestMapperFactory.CreateMapper(), cache.Object);
        await sut.DeleteAsync(id);

        repo.Verify(r => r.Remove(entity), Times.Once);
        cache.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
