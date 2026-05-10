using ApartmentAPI;
using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using ApartmentAPI.Repositories;
using Microsoft.AspNetCore.Http;

namespace ApartmentAPI.Tests;

public class InvoiceRepositoryTests
{
    private static InvoiceListSort Sort() => new(InvoiceSortColumn.CreatedAt, false);

    // F.I.R.S.T — lọc theo căn hộ.
    [Fact]
    public async Task IR01_GetByApartmentIdAsync_ShouldListForApartment_OrderByYearMonthDesc()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new InvoiceRepository(db);
        var apt = await ApartmentTestData.AddApartmentAsync(db, 2001);
        var invOld = await ApartmentTestData.AddInvoiceAsync(db, apt.Id, "old", InvoiceStatus.Paid);
        invOld.Year = 2023;
        invOld.Month = 1;
        var invNew = await ApartmentTestData.AddInvoiceAsync(db, apt.Id, "new", InvoiceStatus.Unpaid);
        invNew.Year = 2024;
        invNew.Month = 6;
        db.Invoices.UpdateRange(invOld, invNew);
        await db.SaveChangesAsync();

        var list = await sut.GetByApartmentIdAsync(apt.Id);

        Assert.Equal(2, list.Count);
        Assert.Equal(invNew.Id, list[0].Id);
        Assert.Equal(invOld.Id, list[1].Id);
    }

    // F.I.R.S.T — phân trang lọc apartmentId + status.
    [Fact]
    public async Task IR02_GetPagedAsync_ShouldFilterApartmentAndStatus()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new InvoiceRepository(db);
        var a1 = await ApartmentTestData.AddApartmentAsync(db, 2101);
        var a2 = await ApartmentTestData.AddApartmentAsync(db, 2102);
        await ApartmentTestData.AddInvoiceAsync(db, a1.Id, "p1", InvoiceStatus.Paid);
        var target = await ApartmentTestData.AddInvoiceAsync(db, a2.Id, "p2", InvoiceStatus.Unpaid);

        var (items, total, _, _) = await sut.GetPagedAsync(1, 10, null, null, a2.Id, InvoiceStatus.Unpaid, null, Sort());

        Assert.Equal(1, total);
        Assert.Equal(target.Id, items[0].Id);
    }

    // F.I.R.S.T — lọc mã hóa đơn chứa chuỗi.
    [Fact]
    public async Task IR03_GetPagedAsync_ShouldFilterInvoiceCodeContains()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new InvoiceRepository(db);
        var apt = await ApartmentTestData.AddApartmentAsync(db, 2201);
        await ApartmentTestData.AddInvoiceAsync(db, apt.Id, "aaa", InvoiceStatus.Unpaid);
        var match = await ApartmentTestData.AddInvoiceAsync(db, apt.Id, "XYZ-SPECIAL", InvoiceStatus.Unpaid);

        var (items, total, _, _) = await sut.GetPagedAsync(1, 10, null, null, null, null, "SPECIAL", Sort());

        Assert.Equal(1, total);
        Assert.Equal(match.Id, items[0].Id);
    }

    // F.I.R.S.T — SoftDelete thiếu Id.
    [Fact]
    public async Task IR04_SoftDeleteAsync_ShouldThrow_WhenMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new InvoiceRepository(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.SoftDeleteAsync(Guid.NewGuid(), "x"));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
    }

    // F.I.R.S.T — Exists sau xóa mềm.
    [Fact]
    public async Task IR05_ExistsAsync_ShouldBeFalse_WhenSoftDeleted()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new InvoiceRepository(db);
        var apt = await ApartmentTestData.AddApartmentAsync(db, 2301);
        var inv = await ApartmentTestData.AddInvoiceAsync(db, apt.Id, "sd", InvoiceStatus.Unpaid);
        inv.IsDeleted = true;
        inv.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Assert.False(await sut.ExistsAsync(inv.Id));
    }
}
