using ApartmentAPI;
using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using ApartmentAPI.Repositories;
using Microsoft.AspNetCore.Http;

namespace ApartmentAPI.Tests;

public class ResidentRepositoryTests
{
    private static ResidentListSort Sort() => new(ResidentSortColumn.CreatedAt, false);

    // F.I.R.S.T — danh sách theo căn hộ.
    [Fact]
    public async Task RR01_GetByApartmentIdAsync_ShouldListResidents()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new ResidentRepository(db);
        var apt = await ApartmentTestData.AddApartmentAsync(db, 3001);
        var r1 = new Resident
        {
            FullName = "A",
            IdentityNumber = "ID-1",
            PhoneNumber = "0900000001",
            ApartmentId = apt.Id,
            CreatedAt = DateTime.UtcNow,
        };
        var r2 = new Resident
        {
            FullName = "B",
            IdentityNumber = "ID-2",
            PhoneNumber = "0900000002",
            ApartmentId = apt.Id,
            CreatedAt = DateTime.UtcNow,
        };
        db.Residents.AddRange(r1, r2);
        await db.SaveChangesAsync();

        var list = await sut.GetByApartmentIdAsync(apt.Id);

        Assert.Equal(2, list.Count);
        Assert.All(list, x => Assert.Equal(apt.Id, x.ApartmentId));
    }

    // F.I.R.S.T — lọc fullName + identity trên phân trang.
    [Fact]
    public async Task RR02_GetPagedAsync_ShouldFilterFullNameAndIdentity()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new ResidentRepository(db);
        var apt = await ApartmentTestData.AddApartmentAsync(db, 3101);
        db.Residents.Add(new Resident
        {
            FullName = "Nguyễn Văn Thử",
            IdentityNumber = "CMND-999",
            PhoneNumber = "0901",
            ApartmentId = apt.Id,
            CreatedAt = DateTime.UtcNow,
        });
        db.Residents.Add(new Resident
        {
            FullName = "Khác",
            IdentityNumber = "CMND-000",
            PhoneNumber = "0902",
            ApartmentId = apt.Id,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var (items, total, _, _) = await sut.GetPagedAsync(1, 10, null, null, apt.Id, "Thử", "999", Sort());

        Assert.Equal(1, total);
        Assert.Contains("Thử", items[0].FullName);
    }

    // F.I.R.S.T — SoftDelete không tồn tại.
    [Fact]
    public async Task RR03_SoftDeleteAsync_ShouldThrow_WhenMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new ResidentRepository(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.SoftDeleteAsync(Guid.NewGuid(), null));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
    }

    // F.I.R.S.T — Cư dân không gán căn vẫn xuất hiện khi không lọc apartmentId (GetAll branch qua GetPaged null).
    [Fact]
    public async Task RR04_GetPagedAsync_WithoutApartmentFilter_ShouldIncludeUnassigned()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new ResidentRepository(db);
        db.Residents.Add(new Resident
        {
            FullName = "Chưa gán căn",
            IdentityNumber = "ID-U",
            PhoneNumber = "0903",
            ApartmentId = null,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var (items, total, _, _) = await sut.GetPagedAsync(1, 10, null, null, null, null, null, Sort());

        Assert.True(total >= 1);
        Assert.Contains(items, r => r.FullName == "Chưa gán căn");
    }
}
