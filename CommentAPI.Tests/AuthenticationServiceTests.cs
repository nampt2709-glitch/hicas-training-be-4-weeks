using System.IdentityModel.Tokens.Jwt;
using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Entities;
using CommentAPI.Interfaces;
using CommentAPI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;

namespace CommentAPI.Tests;

// Unit test AuthenticationService: JWT + IAuthenticationRepository (mock).
public class AuthenticationServiceTests
{
    private static JwtOptions TestJwt => new()
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        SigningKey = new string('k', 48),
        AccessTokenMinutes = 60,
        RefreshTokenDays = 1
    };

    private static AuthenticationService CreateSut(Mock<IAuthenticationRepository> auth)
    {
        return new AuthenticationService(auth.Object, Options.Create(TestJwt));
    }

    // F.I.R.S.T: không lộ thông tin tài khoản.
    // 3A — Arrange: user null. Act: LoginAsync. Assert: 401 LoginFailed.
    [Fact]
    public async Task AS01_LoginAsync_ShouldThrow401_WhenUserNotFound()
    {
        var auth = new Mock<IAuthenticationRepository>();
        auth.Setup(a => a.GetByUserNameAsync("ghost", It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var sut = CreateSut(auth);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.LoginAsync(new LoginRequestDto { UserName = "ghost", Password = "x" }));

        Assert.Equal(StatusCodes.Status401Unauthorized, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.LoginFailed, ex.ErrorCode);
    }

    // F.I.R.S.T: mật khẩu sai cùng mã với user không tồn tại.
    // 3A — Arrange: user có nhưng ValidatePassword false. Act: LoginAsync. Assert: LoginFailed.
    [Fact]
    public async Task AS02_LoginAsync_ShouldThrow401_WhenPasswordInvalid()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "u" };
        var auth = new Mock<IAuthenticationRepository>();
        auth.Setup(a => a.GetByUserNameAsync("u", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        auth.Setup(a => a.ValidatePasswordAsync(user, "wrong", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var sut = CreateSut(auth);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.LoginAsync(new LoginRequestDto { UserName = "u", Password = "wrong" }));

        Assert.Equal(ApiErrorCodes.LoginFailed, ex.ErrorCode);
    }

    // F.I.R.S.T: phát token đủ trường.
    // 3A — Arrange: user + password ok + roles + stamp. Act: LoginAsync. Assert: access/refresh không rỗng; kỳ vọng rỗng cố ý sẽ fail.
    [Fact]
    public async Task AS03_LoginAsync_ShouldReturnTokens_WhenCredentialsValid()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "okuser" };
        var auth = new Mock<IAuthenticationRepository>();
        auth.Setup(a => a.GetByUserNameAsync("okuser", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        auth.Setup(a => a.ValidatePasswordAsync(user, "good", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        auth.Setup(a => a.GetRoleNamesAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "User" });
        auth.Setup(a => a.GetSecurityStampAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync("stamp-ok");

        var sut = CreateSut(auth);
        var tokens = await sut.LoginAsync(new LoginRequestDto { UserName = "okuser", Password = "good" });

        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.True(tokens.AccessTokenExpiresAtUtc > DateTime.UtcNow);
        Assert.NotEqual(string.Empty, tokens.AccessToken);
    }

    // F.I.R.S.T: refresh không đọc được.
    // 3A — Arrange: chuỗi không phải JWT hợp lệ. Act: RefreshAsync. Assert: RefreshFailed.
    [Fact]
    public async Task AS04_RefreshAsync_ShouldThrow401_WhenTokenInvalid()
    {
        var sut = CreateSut(new Mock<IAuthenticationRepository>());
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.RefreshAsync(new RefreshRequestDto { RefreshToken = "not-a-valid-jwt" }));

        Assert.Equal(ApiErrorCodes.RefreshFailed, ex.ErrorCode);
    }

    // F.I.R.S.T: user đã xóa sau khi phát refresh.
    // 3A — Arrange: login ok rồi GetById trả null. Act: RefreshAsync. Assert: RefreshFailed.
    [Fact]
    public async Task AS05_RefreshAsync_ShouldThrow401_WhenUserRemoved()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "r" };
        var auth = new Mock<IAuthenticationRepository>();
        auth.Setup(a => a.GetByUserNameAsync("r", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        auth.Setup(a => a.ValidatePasswordAsync(user, "p", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        auth.Setup(a => a.GetRoleNamesAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        auth.Setup(a => a.GetSecurityStampAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync("s-fixed");

        var sut = CreateSut(auth);
        var login = await sut.LoginAsync(new LoginRequestDto { UserName = "r", Password = "p" });

        auth.Setup(a => a.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.RefreshAsync(new RefreshRequestDto { RefreshToken = login.RefreshToken }));

        Assert.Equal(ApiErrorCodes.RefreshFailed, ex.ErrorCode);
    }

    // F.I.R.S.T: security stamp đổi sau khi đăng nhập (token cũ vô hiệu).
    // 3A — Arrange: stamp đổi biến sau Login. Act: RefreshAsync. Assert: RefreshFailed.
    [Fact]
    public async Task AS06_RefreshAsync_ShouldThrow401_WhenSecurityStampMismatch()
    {
        var stamp = "trước-đổi";
        var user = new User { Id = Guid.NewGuid(), UserName = "st" };
        var auth = new Mock<IAuthenticationRepository>();
        auth.Setup(a => a.GetByUserNameAsync("st", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        auth.Setup(a => a.ValidatePasswordAsync(user, "p", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        auth.Setup(a => a.GetRoleNamesAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "User" });
        auth.Setup(a => a.GetSecurityStampAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns((User _, CancellationToken _) => Task.FromResult(stamp));
        auth.Setup(a => a.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var sut = CreateSut(auth);
        var login = await sut.LoginAsync(new LoginRequestDto { UserName = "st", Password = "p" });

        stamp = "sau-đổi";

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.RefreshAsync(new RefreshRequestDto { RefreshToken = login.RefreshToken }));

        Assert.Equal(ApiErrorCodes.RefreshFailed, ex.ErrorCode);
    }

    // F.I.R.S.T: refresh hợp lệ phát cặp token mới.
    // 3A — Arrange: login + stamp cố định + GetById. Act: RefreshAsync. Assert: access mới khác access cũ.
    [Fact]
    public async Task AS07_RefreshAsync_ShouldIssueNewPair_WhenTokenAndStampValid()
    {
        var stamp = "v1-stamp";
        var user = new User { Id = Guid.NewGuid(), UserName = "stok" };
        var auth = new Mock<IAuthenticationRepository>();
        auth.Setup(a => a.GetByUserNameAsync("stok", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        auth.Setup(a => a.ValidatePasswordAsync(user, "p", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        auth.Setup(a => a.GetRoleNamesAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "User" });
        auth.Setup(a => a.GetSecurityStampAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns((User _, CancellationToken _) => Task.FromResult(stamp));
        auth.Setup(a => a.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var sut = CreateSut(auth);
        var login = await sut.LoginAsync(new LoginRequestDto { UserName = "stok", Password = "p" });
        var rtJwt = new JwtSecurityTokenHandler().ReadJwtToken(login.RefreshToken);
        Assert.True(rtJwt.Payload.TryGetValue("token_type", out var tt) && tt?.ToString() == "refresh",
            $"Refresh JWT phải chứa token_type=refresh; keys={string.Join(",", rtJwt.Payload.Keys)} json={rtJwt.Payload.SerializeToJson()}");

        var next = await sut.RefreshAsync(new RefreshRequestDto { RefreshToken = login.RefreshToken });

        Assert.False(string.IsNullOrEmpty(next.AccessToken));
        Assert.NotEqual(login.AccessToken, next.AccessToken);
    }

    // F.I.R.S.T: idempotent đăng xuất.
    // 3A — Arrange: user không tồn tại. Act: LogoutAsync. Assert: không gọi Revoke.
    [Fact]
    public async Task AS08_LogoutAsync_ShouldBeSilent_WhenUserMissing()
    {
        var id = Guid.NewGuid();
        var auth = new Mock<IAuthenticationRepository>();
        auth.Setup(a => a.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var sut = CreateSut(auth);
        await sut.LogoutAsync(id);

        auth.Verify(a => a.RevokeSessionsAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // F.I.R.S.T: revoke khi user còn.
    // 3A — Arrange: GetById trả user. Act: LogoutAsync. Assert: RevokeSessionsAsync một lần.
    [Fact]
    public async Task AS09_LogoutAsync_ShouldRevoke_WhenUserExists()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "out" };
        var auth = new Mock<IAuthenticationRepository>();
        auth.Setup(a => a.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        auth.Setup(a => a.RevokeSessionsAsync(user, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = CreateSut(auth);
        await sut.LogoutAsync(user.Id);

        auth.Verify(a => a.RevokeSessionsAsync(user, It.IsAny<CancellationToken>()), Times.Once);
    }

    // F.I.R.S.T: lỗi cấu hình Identity (stamp rỗng).
    // 3A — Arrange: sau khi mật khẩu đúng, GetSecurityStamp trả rỗng. Act: LoginAsync. Assert: 500 TokenIssueFailed.
    [Fact]
    public async Task AS10_LoginAsync_ShouldThrow500_WhenSecurityStampEmpty()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "nostamp" };
        var auth = new Mock<IAuthenticationRepository>();
        auth.Setup(a => a.GetByUserNameAsync("nostamp", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        auth.Setup(a => a.ValidatePasswordAsync(user, "p", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        auth.Setup(a => a.GetRoleNamesAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        auth.Setup(a => a.GetSecurityStampAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync("");

        var sut = CreateSut(auth);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            sut.LoginAsync(new LoginRequestDto { UserName = "nostamp", Password = "p" }));

        Assert.Equal(StatusCodes.Status500InternalServerError, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.TokenIssueFailed, ex.ErrorCode);
    }
}
