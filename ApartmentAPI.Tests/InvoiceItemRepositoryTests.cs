using ApartmentAPI;
using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using ApartmentAPI.Repositories;
using Microsoft.AspNetCore.Http;

namespace ApartmentAPI.Tests;

public class InvoiceItemRepositoryTests
{
    private static InvoiceItemListSort Sort() => new(InvoiceItemSortColumn.CreatedAt, false);

    private static async Task<(Invoice Invoice, UtilityService Util)> SeedInvoiceWithUtility(AppDbContext db, string codeSuffix)
    {
        var apt = await ApartmentTestData.AddApartmentAsync(db, Random.Shared.Next(1, 100_000));
        var util = await ApartmentTestData.AddUtilityAsync(db, Random.Shared.Next(1, 100_000));
        var inv = await ApartmentTestData.AddInvoiceAsync(db, apt.Id, codeSuffix);
        return (inv, util);
    }

    // F.I.R.S.T — GetByInvoiceIdAsync.
    [Fact]
    public async Task IIR01_GetByInvoiceIdAsync_ShouldReturnLines()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new InvoiceItemRepository(db);
        var (inv, util) = await SeedInvoiceWithUtility(db, "L1");
        var line = new InvoiceItem
        {
            InvoiceId = inv.Id,
            ServiceId = util.Id,
            Quantity = 2,
            UnitPrice = 5,
            SubTotal = 10,
            CreatedAt = DateTime.UtcNow,
        };
        db.InvoiceItems.Add(line);
        await db.SaveChangesAsync();

        var list = await sut.GetByInvoiceIdAsync(inv.Id);

        var single = Assert.Single(list);
        Assert.Equal(line.Id, single.Id);
    }

    // F.I.R.S.T — lọc invoiceId + serviceId trên phân trang.
    [Fact]
    public async Task IIR02_GetPagedAsync_ShouldFilterInvoiceAndService()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new InvoiceItemRepository(db);
        var (inv, util) = await SeedInvoiceWithUtility(db, "L2");
        var otherUtil = await ApartmentTestData.AddUtilityAsync(db, 888_888);
        db.InvoiceItems.Add(new InvoiceItem
        {
            InvoiceId = inv.Id,
            ServiceId = otherUtil.Id,
            Quantity = 1,
            UnitPrice = 1,
            SubTotal = 1,
            CreatedAt = DateTime.UtcNow,
        });
        var target = new InvoiceItem
        {
            InvoiceId = inv.Id,
            ServiceId = util.Id,
            Quantity = 3,
            UnitPrice = 4,
            SubTotal = 12,
            CreatedAt = DateTime.UtcNow,
        };
        db.InvoiceItems.Add(target);
        await db.SaveChangesAsync();

        var (items, total, _, _) = await sut.GetPagedAsync(1, 10, null, null, inv.Id, util.Id, Sort());

        Assert.Equal(1, total);
        Assert.Equal(target.Id, items[0].Id);
    }

    // F.I.R.S.T — SoftDelete không có bản ghi.
    [Fact]
    public async Task IIR03_SoftDeleteAsync_ShouldThrow_WhenMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new InvoiceItemRepository(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.SoftDeleteAsync(Guid.NewGuid(), "x"));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
    }

    // F.I.R.S.T — Exists false khi xóa mềm.
    [Fact]
    public async Task IIR04_ExistsAsync_ShouldBeFalse_WhenSoftDeleted()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new InvoiceItemRepository(db);
        var (inv, util) = await SeedInvoiceWithUtility(db, "L3");
        var line = new InvoiceItem
        {
            InvoiceId = inv.Id,
            ServiceId = util.Id,
            Quantity = 1,
            UnitPrice = 1,
            SubTotal = 1,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
        };
        db.InvoiceItems.Add(line);
        await db.SaveChangesAsync();

        Assert.False(await sut.ExistsAsync(line.Id));
    }
}
