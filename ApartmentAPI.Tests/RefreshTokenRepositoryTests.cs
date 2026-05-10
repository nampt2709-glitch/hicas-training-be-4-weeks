using ApartmentAPI;
using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using ApartmentAPI.Repositories;
using Microsoft.AspNetCore.Http;

namespace ApartmentAPI.Tests;

public class RefreshTokenRepositoryTests
{
    private static RefreshTokenListSort Sort() => new(RefreshTokenSortColumn.CreatedAt, false);

    // F.I.R.S.T — GetByUserIdAsync.
    [Fact]
    public async Task RTR01_GetByUserIdAsync_ShouldListTokens()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new RefreshTokenRepository(db);
        var user = await ApartmentTestData.AddUserAsync(db, 5001);
        var rt = new RefreshToken
        {
            TokenHash = "hash-ut-1",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = false,
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
        };
        db.RefreshTokens.Add(rt);
        await db.SaveChangesAsync();

        var list = await sut.GetByUserIdAsync(user.Id);

        Assert.Single(list);
    }

    // F.I.R.S.T — phân trang lọc userId + isRevoked.
    [Fact]
    public async Task RTR02_GetPagedAsync_ShouldFilterUserAndRevoked()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new RefreshTokenRepository(db);
        var user = await ApartmentTestData.AddUserAsync(db, 5002);
        db.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = "h-active",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            IsRevoked = false,
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
        });
        db.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = "h-revoked",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            IsRevoked = true,
            RevokedAt = DateTime.UtcNow,
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var (items, total, _, _) = await sut.GetPagedAsync(1, 10, null, null, user.Id, false, Sort());

        Assert.Equal(1, total);
        Assert.False(items[0].IsRevoked);
    }

    // F.I.R.S.T — SoftDelete khi Id không có.
    [Fact]
    public async Task RTR03_SoftDeleteAsync_ShouldThrow_WhenMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new RefreshTokenRepository(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.SoftDeleteAsync(Guid.NewGuid(), null));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
    }

    // F.I.R.S.T — GetPaged không lọc revoked khi null — đủ cả hai bản ghi.
    [Fact]
    public async Task RTR04_GetPagedAsync_WhenIsRevokedNull_ShouldReturnBothStates()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new RefreshTokenRepository(db);
        var user = await ApartmentTestData.AddUserAsync(db, 5003);
        db.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = "a1",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            IsRevoked = false,
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
        });
        db.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = "a2",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            IsRevoked = true,
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var (_, total, _, _) = await sut.GetPagedAsync(1, 10, null, null, user.Id, null, Sort());

        Assert.Equal(2, total);
    }
}
