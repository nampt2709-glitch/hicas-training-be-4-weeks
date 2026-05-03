using AutoMapper;
using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Entities;
using CommentAPI.Interfaces;
using CommentAPI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace CommentAPI.Tests;

// Unit test UserService: IUserRepository, UserManager (mock), cache, mapper.
public class UserServiceTests
{
    private static UserService CreateSut(
        IUserRepository repository,
        UserManager<User> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IMapper mapper,
        IEntityResponseCache cache)
        => new(repository, userManager, roleManager, mapper, cache, TestDbContextFactory.Create());

    // F.I.R.S.T: danh sách user phân trang qua cache.
    // 3A — Arrange: cache hit. Act: GetPagedAsync. Assert: không gọi repository.
    [Fact]
    public async Task US01_GetPagedAsync_ShouldReturnFromCache_WhenHit()
    {
        var cached = new PagedResult<UserDto>
        {
            Items = new List<UserDto>(),
            Page = 1,
            PageSize = 20,
            TotalCount = 0
        };

        var repo = new Mock<IUserRepository>(MockBehavior.Strict);
        var um = UserManagerMockFactory.Create();
        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.GetJsonAsync<PagedResult<UserDto>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var sut = CreateSut(repo.Object, um.Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), cache.Object);
        var r = await sut.GetPagedAsync(1, 20);

        Assert.Same(cached, r);
    }

    // F.I.R.S.T: miss + ghép UserPageRow + roles batch.
    // 3A — Arrange: một dòng projection, roles dict có Admin. Act: GetPagedAsync. Assert: DTO có đúng role.
    [Fact]
    public async Task US02_GetPagedAsync_ShouldMapRows_AndMergeRoles_WhenCacheMiss()
    {
        var id = Guid.NewGuid();
        var row = new UserPageRow(id, "Tên", "login1", "a@b.c", DateTime.UtcNow);

        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetPagedAsync(1, 10, It.IsAny<CancellationToken>(), null, null, null, null, null))
            .ReturnsAsync((new List<UserPageRow> { row }, 1L));
        repo.Setup(r => r.GetRoleNamesByUserIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, List<string>> { [id] = new List<string> { "Admin", "User" } });

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.GetJsonAsync<PagedResult<UserDto>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PagedResult<UserDto>?)null);
        cache.Setup(c => c.SetJsonAsync(It.IsAny<string>(), It.IsAny<PagedResult<UserDto>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(repo.Object, UserManagerMockFactory.Create().Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), cache.Object);
        var r = await sut.GetPagedAsync(1, 10);

        Assert.Single(r.Items);
        Assert.Equal(id, r.Items[0].Id);
        Assert.Equal(new[] { "Admin", "User" }, r.Items[0].Roles);
        Assert.NotEqual(Guid.Empty, r.Items[0].Id);
    }

    // F.I.R.S.T: filter name — không cache danh sách (tránh khóa sai).
    // 3A — Arrange: repo trả một dòng khi nameContains khác rỗng; cache Strict (không được gọi đọc/ghi list). Act: GetPagedAsync có name. Assert: có dữ liệu; không SetJson list.
    [Fact]
    public async Task US03_GetPagedAsync_ShouldQueryRepo_AndNotCacheList_WhenNameFilter()
    {
        var id = Guid.NewGuid();
        var row = new UserPageRow(id, "Nguyên", "login1", "a@b.c", DateTime.UtcNow);

        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetPagedAsync(1, 5, It.IsAny<CancellationToken>(), null, null, "Nguyên", null, null))
            .ReturnsAsync((new List<UserPageRow> { row }, 1L));
        repo.Setup(r => r.GetRoleNamesByUserIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, List<string>> { [id] = new List<string> { "User" } });

        var cache = new Mock<IEntityResponseCache>(MockBehavior.Strict);

        var sut = CreateSut(repo.Object, UserManagerMockFactory.Create().Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), cache.Object);
        var r = await sut.GetPagedAsync(1, 5, CancellationToken.None, null, null, "Nguyên", null, null);

        Assert.Single(r.Items);
        Assert.Equal(id, r.Items[0].Id);
        cache.Verify(c => c.SetJsonAsync(It.IsAny<string>(), It.IsAny<PagedResult<UserDto>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // F.I.R.S.T: filter userName — cùng quy tắc bỏ cache list.
    // 3A — Arrange: userNameContains "adm". Act: GetPagedAsync. Assert: repo được gọi đúng tham số; không ghi cache phân trang.
    [Fact]
    public async Task US04_GetPagedAsync_ShouldQueryRepo_AndNotCacheList_WhenUserNameFilter()
    {
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetPagedAsync(2, 10, It.IsAny<CancellationToken>(), null, null, null, "adm", null))
            .ReturnsAsync((new List<UserPageRow>(), 0L));
        repo.Setup(r => r.GetRoleNamesByUserIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, List<string>>());

        var cache = new Mock<IEntityResponseCache>(MockBehavior.Strict);

        var sut = CreateSut(repo.Object, UserManagerMockFactory.Create().Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), cache.Object);
        var r = await sut.GetPagedAsync(2, 10, CancellationToken.None, null, null, null, "adm", null);

        Assert.Empty(r.Items);
        cache.Verify(c => c.SetJsonAsync(It.IsAny<string>(), It.IsAny<PagedResult<UserDto>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // F.I.R.S.T: chi tiết user cache hit.
    // 3A — Arrange: cache có UserDto. Act: GetByIdAsync. Assert: không đọc repository.
    [Fact]
    public async Task US05_GetByIdAsync_ShouldReturnFromCache_WhenHit()
    {
        var id = Guid.NewGuid();
        var cached = new UserDto
        {
            Id = id,
            Name = "n",
            UserName = "u",
            Email = "e@e.e",
            Roles = new List<string>(),
            CreatedAt = DateTime.UtcNow
        };

        var repo = new Mock<IUserRepository>(MockBehavior.Strict);
        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.GetJsonAsync<UserDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var sut = CreateSut(repo.Object, UserManagerMockFactory.Create().Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), cache.Object);
        var r = await sut.GetByIdAsync(id);

        Assert.Same(cached, r);
    }

    // F.I.R.S.T: 404 user.
    // 3A — Arrange: miss + repo null. Act: GetByIdAsync. Assert: UserNotFound.
    [Fact]
    public async Task US06_GetByIdAsync_ShouldThrow404_WhenMissing()
    {
        var id = Guid.NewGuid();
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((User?)null);

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.GetJsonAsync<UserDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserDto?)null);

        var sut = CreateSut(repo.Object, UserManagerMockFactory.Create().Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), cache.Object);
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.GetByIdAsync(id));

        Assert.Equal(ApiErrorCodes.UserNotFound, ex.ErrorCode);
    }

    // F.I.R.S.T: miss + Map + roles từ UserManager.
    // 3A — Arrange: entity + GetRolesAsync. Act: GetByIdAsync. Assert: Roles khớp; kỳ vọng role sai sẽ fail.
    [Fact]
    public async Task US07_GetByIdAsync_ShouldPopulateRoles_WhenCacheMiss()
    {
        var id = Guid.NewGuid();
        var entity = new User
        {
            Id = id,
            Name = "A",
            UserName = "a",
            Email = "a@a.a",
            CreatedAt = DateTime.UtcNow
        };

        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.GetJsonAsync<UserDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserDto?)null);
        cache.Setup(c => c.SetJsonAsync(It.IsAny<string>(), It.IsAny<UserDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var um = UserManagerMockFactory.Create();
        um.Setup(m => m.GetRolesAsync(entity)).ReturnsAsync(new List<string> { "User" });

        var sut = CreateSut(repo.Object, um.Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), cache.Object);
        var dto = await sut.GetByIdAsync(id);

        Assert.Single(dto.Roles);
        Assert.Equal("User", dto.Roles[0]);
        Assert.DoesNotContain("Admin", dto.Roles);
    }

    // F.I.R.S.T: trùng UserName.
    // 3A — Arrange: FindByName trả user. Act: CreateAsync. Assert: 409 UserNameConflict.
    [Fact]
    public async Task US08_CreateAsync_ShouldThrow409_WhenUserNameTaken()
    {
        var um = UserManagerMockFactory.Create();
        um.Setup(m => m.FindByNameAsync("taken"))
            .ReturnsAsync(new User { Id = Guid.NewGuid(), UserName = "taken" });

        var sut = CreateSut(Mock.Of<IUserRepository>(), um.Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.CreateAsync(new CreateUserDto { Name = "N", UserName = "taken", Password = "P@ssw0rd!", Email = "e@e.e" }));

        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.UserNameConflict, ex.ErrorCode);
    }

    // F.I.R.S.T: tạo user + role mặc định.
    // 3A — Arrange: username trống, Create + AddToRole + GetRoles. Act: CreateAsync. Assert: có role User.
    [Fact]
    public async Task US09_CreateAsync_ShouldSucceed_WhenIdentityAccepts()
    {
        var um = UserManagerMockFactory.Create();
        um.Setup(m => m.FindByNameAsync(It.IsAny<string>()))
            .ReturnsAsync((User?)null);
        um.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        um.Setup(m => m.AddToRoleAsync(It.IsAny<User>(), "User"))
            .ReturnsAsync(IdentityResult.Success);
        um.Setup(m => m.GetRolesAsync(It.IsAny<User>()))
            .ReturnsAsync(new List<string> { "User" });

        var sut = CreateSut(Mock.Of<IUserRepository>(), um.Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var dto = await sut.CreateAsync(new CreateUserDto
        {
            Name = "Người mới",
            UserName = "newuser",
            Password = "P@ssw0rd!Long",
            Email = "x@y.z"
        });

        Assert.Equal("newuser", dto.UserName);
        Assert.Contains("User", dto.Roles);
    }

    // F.I.R.S.T: Identity trả lỗi tạo.
    // 3A — Arrange: CreateAsync Failed. Act: CreateAsync. Assert: 400 UserCreateFailed.
    [Fact]
    public async Task US10_CreateAsync_ShouldThrow400_WhenIdentityCreateFails()
    {
        var um = UserManagerMockFactory.Create();
        um.Setup(m => m.FindByNameAsync(It.IsAny<string>()))
            .ReturnsAsync((User?)null);
        um.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "mật khẩu yếu" }));

        var sut = CreateSut(Mock.Of<IUserRepository>(), um.Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.CreateAsync(new CreateUserDto { Name = "a", UserName = "b", Password = "x", Email = null }));

        Assert.Equal(ApiErrorCodes.UserCreateFailed, ex.ErrorCode);
        Assert.Contains("yếu", ex.ClientMessage);
    }

    // F.I.R.S.T: user tự sửa — không có entity.
    // 3A — Arrange: repo null, currentUser = id. Act: UpdateAsSelfAsync. Assert: UserNotFound.
    [Fact]
    public async Task US11_UpdateAsSelfAsync_ShouldThrow404_WhenMissing()
    {
        var id = Guid.NewGuid();
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((User?)null);

        var sut = CreateSut(repo.Object, UserManagerMockFactory.Create().Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateAsSelfAsync(id, new UpdateUserDto { Name = "x" }, id));

        Assert.Equal(ApiErrorCodes.UserNotFound, ex.ErrorCode);
    }

    // F.I.R.S.T: user tự sửa — sai id so với JWT.
    // 3A — Arrange: id khác currentUserId. Act: UpdateAsSelfAsync. Assert: 403 NotResourceAuthor.
    [Fact]
    public async Task US12_UpdateAsSelfAsync_ShouldThrow403_WhenNotSelf()
    {
        var id = Guid.NewGuid();
        var sut = CreateSut(
            Mock.Of<IUserRepository>(MockBehavior.Strict),
            UserManagerMockFactory.Create().Object,
            RoleManagerMockFactory.Create().Object,
            TestMapperFactory.CreateMapper(),
            Mock.Of<IEntityResponseCache>(MockBehavior.Strict));

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.UpdateAsSelfAsync(id, new UpdateUserDto { Name = "x" }, Guid.NewGuid()));

        Assert.Equal(StatusCodes.Status403Forbidden, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.NotResourceAuthor, ex.ErrorCode);
    }

    // F.I.R.S.T: cập nhật Name khi đúng chủ tài khoản + xóa cache chi tiết.
    // 3A — Arrange: entity tồn tại. Act: UpdateAsSelfAsync. Assert: Name đổi, RemoveAsync cache.
    [Fact]
    public async Task US13_UpdateAsSelfAsync_ShouldPersist_AndInvalidateCache_WhenFound()
    {
        var id = Guid.NewGuid();
        var entity = new User { Id = id, Name = "old", UserName = "u", Email = "e@e.e", CreatedAt = DateTime.UtcNow };

        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);
        repo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = CreateSut(repo.Object, UserManagerMockFactory.Create().Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), cache.Object);
        await sut.UpdateAsSelfAsync(id, new UpdateUserDto { Name = "mới" }, id);

        Assert.Equal("mới", entity.Name);
        cache.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // F.I.R.S.T: xóa user.
    // 3A — Arrange: không có entity. Act: DeleteAsync. Assert: UserNotFound.
    [Fact]
    public async Task US14_DeleteAsync_ShouldThrow404_WhenMissing()
    {
        var id = Guid.NewGuid();
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((User?)null);

        var sut = CreateSut(repo.Object, UserManagerMockFactory.Create().Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.DeleteAsync(id));

        Assert.Equal(ApiErrorCodes.UserNotFound, ex.ErrorCode);
    }

    // F.I.R.S.T: Identity Delete thất bại.
    // 3A — Arrange: entity + DeleteAsync Failed. Act: DeleteAsync. Assert: UserDeleteFailed.
    [Fact]
    public async Task US15_DeleteAsync_ShouldThrow400_WhenIdentityDeleteFails()
    {
        var id = Guid.NewGuid();
        var entity = new User { Id = id, Name = "a", UserName = "b", Email = "c@c.c", CreatedAt = DateTime.UtcNow };

        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);

        var um = UserManagerMockFactory.Create();
        um.Setup(m => m.DeleteAsync(entity))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "không xóa được" }));

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = CreateSut(repo.Object, um.Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), cache.Object);
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.DeleteAsync(id));

        Assert.Equal(ApiErrorCodes.UserDeleteFailed, ex.ErrorCode);
    }

    // F.I.R.S.T: xóa thành công.
    // 3A — Arrange: entity + Delete Success. Act: DeleteAsync. Assert: không ném.
    [Fact]
    public async Task US16_DeleteAsync_ShouldSucceed_WhenIdentityAccepts()
    {
        var id = Guid.NewGuid();
        var entity = new User { Id = id, Name = "a", UserName = "b", Email = "c@c.c", CreatedAt = DateTime.UtcNow };

        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);

        var um = UserManagerMockFactory.Create();
        um.Setup(m => m.DeleteAsync(entity)).ReturnsAsync(IdentityResult.Success);

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = CreateSut(repo.Object, um.Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), cache.Object);
        await sut.DeleteAsync(id);

        um.Verify(m => m.DeleteAsync(entity), Times.Once);
    }

    // F.I.R.S.T: admin đổi username — trùng user khác.
    // 3A — Arrange: FindByName trả user khác id. Act: UpdateAsAdminAsync. Assert: 409 UserNameConflict.
    [Fact]
    public async Task US17_UpdateAsAdmin_ShouldThrow409_WhenUserNameTakenByOther()
    {
        var id = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var entity = new User { Id = id, Name = "a", UserName = "old", Email = "a@a.a", CreatedAt = DateTime.UtcNow };

        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);

        var um = UserManagerMockFactory.Create();
        um.Setup(m => m.FindByNameAsync("taken"))
            .ReturnsAsync(new User { Id = otherId, UserName = "taken" });

        var sut = CreateSut(repo.Object, um.Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateAsAdminAsync(id, new AdminUpdateUserDto
        {
            Name = "n",
            UserName = "taken",
            Email = "e@e.e",
            Roles = new List<string> { "User" }
        }));

        Assert.Equal(ApiErrorCodes.UserNameConflict, ex.ErrorCode);
    }

    // F.I.R.S.T: admin đổi email — trùng tài khoản khác.
    // 3A — Arrange: FindByEmail trả user khác. Act: UpdateAsAdminAsync. Assert: UserEmailConflict.
    [Fact]
    public async Task US18_UpdateAsAdmin_ShouldThrow409_WhenEmailTakenByOther()
    {
        var id = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var entity = new User { Id = id, Name = "a", UserName = "u1", Email = "a@a.a", CreatedAt = DateTime.UtcNow };

        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);

        var um = UserManagerMockFactory.Create();
        um.Setup(m => m.FindByNameAsync("u1")).ReturnsAsync(entity);
        um.Setup(m => m.FindByEmailAsync("dup@x.com"))
            .ReturnsAsync(new User { Id = otherId, Email = "dup@x.com" });

        var sut = CreateSut(repo.Object, um.Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateAsAdminAsync(id, new AdminUpdateUserDto
        {
            Name = "n",
            UserName = "u1",
            Email = "dup@x.com",
            Roles = new List<string> { "User" }
        }));

        Assert.Equal(ApiErrorCodes.UserEmailConflict, ex.ErrorCode);
    }

    // F.I.R.S.T: không gỡ Admin khỏi admin duy nhất.
    // 3A — Arrange: user hiện Admin, GetUsersInRole chỉ một người. Act: UpdateAsAdmin chỉ User. Assert: UserLastAdminProtected.
    [Fact]
    public async Task US19_UpdateAsAdmin_ShouldThrow400_WhenDemotingLastAdmin()
    {
        var id = Guid.NewGuid();
        var entity = new User { Id = id, Name = "a", UserName = "solo", Email = "a@a.a", CreatedAt = DateTime.UtcNow };

        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);

        var um = UserManagerMockFactory.Create();
        um.Setup(m => m.FindByNameAsync("solo")).ReturnsAsync(entity);
        um.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        um.Setup(m => m.GetRolesAsync(entity)).ReturnsAsync(new List<string> { "Admin" });
        um.Setup(m => m.GetUsersInRoleAsync("Admin")).ReturnsAsync(new List<User> { entity });

        var sut = CreateSut(repo.Object, um.Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), Mock.Of<IEntityResponseCache>());
        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateAsAdminAsync(id, new AdminUpdateUserDto
        {
            Name = "a",
            UserName = "solo",
            Email = "a@a.a",
            Roles = new List<string> { "User" }
        }));

        Assert.Equal(ApiErrorCodes.UserLastAdminProtected, ex.ErrorCode);
    }

    // F.I.R.S.T: admin cập nhật đầy đủ — Identity chấp nhận mọi bước.
    // 3A — Arrange: mock Find*, SetUserName, SetEmail, roles. Act: UpdateAsAdminAsync. Assert: cache Remove.
    [Fact]
    public async Task US20_UpdateAsAdmin_ShouldSucceed_WhenIdentityAccepts()
    {
        var id = Guid.NewGuid();
        var entity = new User { Id = id, Name = "old", UserName = "u1", Email = "o@o.o", CreatedAt = DateTime.UtcNow };

        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);

        var um = UserManagerMockFactory.Create();
        um.Setup(m => m.FindByNameAsync("u2")).ReturnsAsync((User?)null);
        um.Setup(m => m.FindByEmailAsync("u2@users.local")).ReturnsAsync((User?)null);
        um.Setup(m => m.GetRolesAsync(entity)).ReturnsAsync(new List<string> { "User" });
        um.Setup(m => m.SetUserNameAsync(entity, "u2")).ReturnsAsync(IdentityResult.Success);
        um.Setup(m => m.SetEmailAsync(entity, "u2@users.local")).ReturnsAsync(IdentityResult.Success);
        um.Setup(m => m.RemoveFromRolesAsync(entity, It.IsAny<IEnumerable<string>>())).ReturnsAsync(IdentityResult.Success);
        um.Setup(m => m.AddToRolesAsync(entity, It.IsAny<IEnumerable<string>>())).ReturnsAsync(IdentityResult.Success);

        var cache = new Mock<IEntityResponseCache>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = CreateSut(repo.Object, um.Object, RoleManagerMockFactory.Create().Object, TestMapperFactory.CreateMapper(), cache.Object);
        await sut.UpdateAsAdminAsync(id, new AdminUpdateUserDto
        {
            Name = "new",
            UserName = "u2",
            Email = null,
            Roles = new List<string> { "User", "Admin" }
        });

        Assert.Equal("new", entity.Name);
        cache.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
