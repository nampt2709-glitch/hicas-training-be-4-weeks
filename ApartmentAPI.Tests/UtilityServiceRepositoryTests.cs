using ApartmentAPI;
using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using ApartmentAPI.Repositories;
using Microsoft.AspNetCore.Http;

namespace ApartmentAPI.Tests;

public class UtilityServiceRepositoryTests
{
    private static UtilityListSort Sort() => new(UtilitySortColumn.Name, false);

    // F.I.R.S.T — GetActiveAsync chỉ IsActive true, sắp Name.
    [Fact]
    public async Task UR01_GetActiveAsync_ShouldExcludeInactive_OrderByName()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new UtilityServiceRepository(db);
        var activeB = ApartmentTestData.CreateUtility(4001, true);
        activeB.Name = "B-active";
        var activeA = ApartmentTestData.CreateUtility(4002, true);
        activeA.Name = "A-active";
        var inactive = ApartmentTestData.CreateUtility(4003, false);
        inactive.Name = "C-off";
        db.UtilityServices.AddRange(activeB, activeA, inactive);
        await db.SaveChangesAsync();

        var list = await sut.GetActiveAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal("A-active", list[0].Name);
        Assert.Equal("B-active", list[1].Name);
    }

    // F.I.R.S.T — phân trang lọc isActive + name chứa.
    [Fact]
    public async Task UR02_GetPagedAsync_ShouldFilterActiveAndName()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new UtilityServiceRepository(db);
        db.UtilityServices.Add(ApartmentTestData.CreateUtility(4101, true)); // generic name DV thử 4101
        var match = ApartmentTestData.CreateUtility(4102, true);
        match.Name = "Dịch vụ ĐẶC-BIỆT-UT";
        db.UtilityServices.Add(match);
        await db.SaveChangesAsync();

        var (items, total, _, _) = await sut.GetPagedAsync(1, 10, null, null, true, "ĐẶC-BIỆT", Sort());

        Assert.Equal(1, total);
        Assert.Equal(match.Id, items[0].Id);
    }

    // F.I.R.S.T — SoftDelete không tồn tại.
    [Fact]
    public async Task UR03_SoftDeleteAsync_ShouldThrow_WhenMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new UtilityServiceRepository(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.SoftDeleteAsync(Guid.NewGuid(), null));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
    }
}
