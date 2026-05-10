using ApartmentAPI;
using ApartmentAPI.Data;
using ApartmentAPI.DTOs;
using ApartmentAPI.Entities;
using ApartmentAPI.Repositories;
using Microsoft.AspNetCore.Http;

namespace ApartmentAPI.Tests;

// Kiểm thử ApartmentRepository trên InMemory (không cần SQL thật).
public class ApartmentRepositoryTests
{
    // F.I.R.S.T — độc lập, lặp lại được, tự kiểm tra.
    // 3A — Arrange: thêm hai căn hộ khác trạng thái; Act: GetByStatusAsync(Occupied); Assert: chỉ trả về đúng bản ghi khớp.
    [Fact]
    public async Task AR01_GetByStatusAsync_ShouldReturnOnlyMatchingStatus()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new ApartmentRepository(db);

        var occupied = new Apartment
        {
            Floor = 2,
            RoomNumber = "0201",
            Area = 70,
            Status = ApartmentStatus.Occupied,
            MaxResidents = 4,
        };
        var available = new Apartment
        {
            Floor = 2,
            RoomNumber = "0202",
            Area = 65,
            Status = ApartmentStatus.Available,
            MaxResidents = 3,
        };

        db.Apartments.AddRange(occupied, available);
        await db.SaveChangesAsync();

        var result = await sut.GetByStatusAsync(ApartmentStatus.Occupied);

        var single = Assert.Single(result);
        Assert.Equal(occupied.Id, single.Id);
    }

    // F.I.R.S.T — nhanh, một hành vi rõ ràng.
    // 3A — Arrange: soft delete bản ghi; Act: GetByIdAsync; Assert: null vì global query filter.
    [Fact]
    public async Task AR02_GetByIdAsync_ShouldReturnNull_WhenSoftDeleted()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new ApartmentRepository(db);

        var entity = new Apartment
        {
            Floor = 5,
            RoomNumber = "0501",
            Area = 88,
            Status = ApartmentStatus.Maintenance,
            MaxResidents = 5,
        };
        await db.Apartments.AddAsync(entity);
        await db.SaveChangesAsync();

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        db.Apartments.Update(entity);
        await db.SaveChangesAsync();

        var found = await sut.GetByIdAsync(entity.Id);

        Assert.Null(found);
    }

    // F.I.R.S.T — minh chứng ExistsAsync với bản ghi còn hiệu lực.
    // 3A — Arrange: một Apartment; Act/Assert: ExistsAsync true.
    [Fact]
    public async Task AR03_ExistsAsync_ShouldReturnTrue_ForActiveEntity()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new ApartmentRepository(db);

        var entity = new Apartment
        {
            Floor = 1,
            RoomNumber = "0101",
            Area = 55,
            Status = ApartmentStatus.Available,
            MaxResidents = 2,
        };
        await db.Apartments.AddAsync(entity);
        await db.SaveChangesAsync();

        Assert.True(await sut.ExistsAsync(entity.Id));
    }

    // F.I.R.S.T — một hành vi đọc theo Id.
    // 3A — Arrange thêm căn hộ; Act: GetByIdAsync; Assert: khớp Id và RoomNumber.
    [Fact]
    public async Task AR04_GetByIdAsync_ShouldReturnEntity_WhenActive()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new ApartmentRepository(db);
        var entity = ApartmentTestData.CreateApartment(401);
        db.Apartments.Add(entity);
        await db.SaveChangesAsync();

        var found = await sut.GetByIdAsync(entity.Id);

        Assert.NotNull(found);
        Assert.Equal(entity.RoomNumber, found!.RoomNumber);
    }

    // F.I.R.S.T — trật tự GetAll theo CreatedAt tăng.
    // 3A — Arrange hai bản ghi CreatedAt khác nhau; Act: GetAllAsync; Assert: thứ tự cũ trước mới sau.
    [Fact]
    public async Task AR05_GetAllAsync_ShouldOrderByCreatedAtAscending()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new ApartmentRepository(db);
        var older = ApartmentTestData.CreateApartment(502);
        older.CreatedAt = DateTime.UtcNow.AddDays(-2);
        var newer = ApartmentTestData.CreateApartment(503);
        newer.CreatedAt = DateTime.UtcNow.AddDays(-1);
        db.Apartments.AddRange(older, newer);
        await db.SaveChangesAsync();

        var all = await sut.GetAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal(older.Id, all[0].Id);
        Assert.Equal(newer.Id, all[1].Id);
    }

    // F.I.R.S.T — lọc phân trang theo trạng thái.
    // 3A — Arrange Available + Occupied; Act: GetPagedAsync status Available; Assert: một dòng và TotalCount = 1.
    [Fact]
    public async Task AR06_GetPagedAsync_ShouldFilterByStatus()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new ApartmentRepository(db);
        db.Apartments.Add(ApartmentTestData.CreateApartment(601, ApartmentStatus.Available));
        db.Apartments.Add(ApartmentTestData.CreateApartment(602, ApartmentStatus.Occupied));
        await db.SaveChangesAsync();

        var sort = new ApartmentListSort(ApartmentSortColumn.CreatedAt, false);
        var (items, total, _, _) = await sut.GetPagedAsync(1, 20, null, null, ApartmentStatus.Available, null, sort);

        Assert.Equal(1, total);
        var single = Assert.Single(items);
        Assert.Equal(ApartmentStatus.Available, single.Status);
    }

    // F.I.R.S.T — lọc theo chuỗi con số phòng.
    // 3A — Arrange hai RoomNumber; Act: roomNumberContains "T603"; Assert: chỉ khớp một.
    [Fact]
    public async Task AR07_GetPagedAsync_ShouldFilterByRoomNumberContains()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new ApartmentRepository(db);
        var a = ApartmentTestData.CreateApartment(603);
        a.RoomNumber = "X603Y";
        var b = ApartmentTestData.CreateApartment(604);
        b.RoomNumber = "OTHER";
        db.Apartments.AddRange(a, b);
        await db.SaveChangesAsync();

        var sort = new ApartmentListSort(ApartmentSortColumn.RoomNumber, false);
        var (items, total, _, _) = await sut.GetPagedAsync(1, 10, null, null, null, "603", sort);

        Assert.Equal(1, total);
        Assert.Equal(a.Id, items[0].Id);
    }

    // F.I.R.S.T — khoảng CreatedAt bao gồm biên.
    // 3A — Arrange một bản ghi giữa from/to và một ngoài; Act: GetPagedAsync; Assert: chỉ một dòng.
    [Fact]
    public async Task AR08_GetPagedAsync_ShouldFilterCreatedAtRange()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new ApartmentRepository(db);
        var inside = ApartmentTestData.CreateApartment(701);
        inside.CreatedAt = new DateTime(2025, 3, 15, 12, 0, 0, DateTimeKind.Utc);
        var outside = ApartmentTestData.CreateApartment(702);
        outside.CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Apartments.AddRange(inside, outside);
        await db.SaveChangesAsync();

        var from = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 3, 31, 0, 0, 0, DateTimeKind.Utc);
        var sort = new ApartmentListSort(ApartmentSortColumn.CreatedAt, false);
        var (items, total, _, _) = await sut.GetPagedAsync(1, 10, from, to, null, null, sort);

        Assert.Equal(1, total);
        Assert.Equal(inside.Id, items[0].Id);
    }

    // F.I.R.S.T — trang 2 với pageSize nhỏ.
    // 3A — Arrange ba căn sắp theo RoomNumber asc; Act: page 2 size 1 sort RoomNumber; Assert: phần tử thứ hai.
    [Fact]
    public async Task AR09_GetPagedAsync_ShouldReturnSecondPage()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new ApartmentRepository(db);
        for (var i = 0; i < 3; i++)
        {
            var a = ApartmentTestData.CreateApartment(800 + i);
            a.RoomNumber = $"P{i}";
            a.CreatedAt = DateTime.UtcNow.AddMinutes(-i);
            db.Apartments.Add(a);
        }

        await db.SaveChangesAsync();

        var sort = new ApartmentListSort(ApartmentSortColumn.RoomNumber, false);
        var (items, total, page, size) = await sut.GetPagedAsync(2, 1, null, null, null, null, sort);

        Assert.Equal(3, total);
        Assert.Equal(2, page);
        Assert.Equal(1, size);
        var single = Assert.Single(items);
        Assert.Equal("P1", single.RoomNumber);
    }

    // F.I.R.S.T — ExistsAsync không thấy bản ghi đã xóa mềm.
    // 3A — Arrange soft delete; Act: ExistsAsync; Assert: false.
    [Fact]
    public async Task AR10_ExistsAsync_ShouldReturnFalse_WhenSoftDeleted()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new ApartmentRepository(db);
        var entity = ApartmentTestData.CreateApartment(901);
        db.Apartments.Add(entity);
        await db.SaveChangesAsync();
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Assert.False(await sut.ExistsAsync(entity.Id));
    }

    // F.I.R.S.T — SoftDeleteAsync khi Id không tồn tại.
    // 3A — Arrange DB rỗng; Act/Assert: ApiException NOT_FOUND.
    [Fact]
    public async Task AR11_SoftDeleteAsync_ShouldThrow_WhenMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new ApartmentRepository(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.SoftDeleteAsync(Guid.NewGuid(), "admin"));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.NotFound, ex.ErrorCode);
    }

    // F.I.R.S.T — SoftDeleteAsync gán cờ và cần SaveChanges.
    // 3A — Arrange một căn; Act: SoftDeleteAsync + SaveChanges; Assert: GetById trả null do filter.
    [Fact]
    public async Task AR12_SoftDeleteAsync_ShouldMarkDeleted_WhenExists()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new ApartmentRepository(db);
        var entity = ApartmentTestData.CreateApartment(902);
        db.Apartments.Add(entity);
        await db.SaveChangesAsync();

        await sut.SoftDeleteAsync(entity.Id, "tester");
        await sut.SaveChangesAsync();

        Assert.Null(await sut.GetByIdAsync(entity.Id));
    }
}
