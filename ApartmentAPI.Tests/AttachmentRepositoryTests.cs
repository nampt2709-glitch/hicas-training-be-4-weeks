using ApartmentAPI;
using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using ApartmentAPI.Repositories;
using Microsoft.AspNetCore.Http;

namespace ApartmentAPI.Tests;

public class AttachmentRepositoryTests
{
    // F.I.R.S.T — GetByUserIdAsync.
    [Fact]
    public async Task ATR01_GetByUserIdAsync_ShouldListForUser()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new AttachmentRepository(db);
        var user = await ApartmentTestData.AddUserAsync(db, 1);
        var att = new Attachment
        {
            Scope = AttachmentScope.Avatar,
            OriginalFileName = "a.png",
            StoredFileName = "s.png",
            FilePath = "/a.png",
            ContentType = "image/png",
            FileSize = 100,
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
        };
        db.Attachments.Add(att);
        await db.SaveChangesAsync();

        var list = await sut.GetByUserIdAsync(user.Id);

        var single = Assert.Single(list);
        Assert.Equal(att.Id, single.Id);
    }

    // F.I.R.S.T — GetByFeedbackIdAsync.
    [Fact]
    public async Task ATR02_GetByFeedbackIdAsync_ShouldListForFeedback()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new AttachmentRepository(db);
        var user = await ApartmentTestData.AddUserAsync(db, 2);
        var fb = await ApartmentTestData.AddFeedbackAsync(db, user.Id, null, 1);
        var att = new Attachment
        {
            Scope = AttachmentScope.Feedback,
            OriginalFileName = "f.png",
            StoredFileName = "sf.png",
            FilePath = "/f.png",
            ContentType = "image/png",
            FileSize = 200,
            UserId = user.Id,
            FeedbackId = fb.Id,
            CreatedAt = DateTime.UtcNow,
        };
        db.Attachments.Add(att);
        await db.SaveChangesAsync();

        var list = await sut.GetByFeedbackIdAsync(fb.Id);

        Assert.Single(list);
    }

    // F.I.R.S.T — GetByScopeAsync.
    [Fact]
    public async Task ATR03_GetByScopeAsync_ShouldFilterScope()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new AttachmentRepository(db);
        var user = await ApartmentTestData.AddUserAsync(db, 3);
        db.Attachments.Add(new Attachment
        {
            Scope = AttachmentScope.Avatar,
            OriginalFileName = "av.png",
            StoredFileName = "1.png",
            FilePath = "/1.png",
            ContentType = "image/png",
            FileSize = 1,
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
        });
        var fb = await ApartmentTestData.AddFeedbackAsync(db, user.Id, null, 2);
        db.Attachments.Add(new Attachment
        {
            Scope = AttachmentScope.Feedback,
            OriginalFileName = "fb.png",
            StoredFileName = "2.png",
            FilePath = "/2.png",
            ContentType = "image/png",
            FileSize = 2,
            UserId = user.Id,
            FeedbackId = fb.Id,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var avatars = await sut.GetByScopeAsync(AttachmentScope.Avatar);

        Assert.Single(avatars);
        Assert.Equal(AttachmentScope.Avatar, avatars[0].Scope);
    }

    // F.I.R.S.T — phân trang lọc userId + feedbackId + scope + tên file.
    [Fact]
    public async Task ATR04_GetPagedAsync_ShouldCombineFilters()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new AttachmentRepository(db);
        var user = await ApartmentTestData.AddUserAsync(db, 4);
        var fb = await ApartmentTestData.AddFeedbackAsync(db, user.Id, null, 3);
        var noise = new Attachment
        {
            Scope = AttachmentScope.Feedback,
            OriginalFileName = "other.bin",
            StoredFileName = "o.bin",
            FilePath = "/o",
            ContentType = "application/octet-stream",
            FileSize = 1,
            UserId = user.Id,
            FeedbackId = fb.Id,
            CreatedAt = DateTime.UtcNow,
        };
        var target = new Attachment
        {
            Scope = AttachmentScope.Feedback,
            OriginalFileName = "report-UT-99.pdf",
            StoredFileName = "r.pdf",
            FilePath = "/r",
            ContentType = "application/pdf",
            FileSize = 9,
            UserId = user.Id,
            FeedbackId = fb.Id,
            CreatedAt = DateTime.UtcNow,
        };
        db.Attachments.AddRange(noise, target);
        await db.SaveChangesAsync();

        // BƯỚC 1 — Tham số sort (AttachmentListSort) là bắt buộc; dùng đặt tên + tuple có field để tránh lỗi suy luận kiểu (var (...) không suy ra được khi chữ ký lệch).
        var result = await sut.GetPagedAsync(
            page: 1,
            pageSize: 10,
            createdAtFrom: null,
            createdAtTo: null,
            userId: user.Id,
            feedbackId: fb.Id,
            postId: null,
            scope: AttachmentScope.Feedback,
            originalFileNameContains: "UT-99",
            sort: new AttachmentListSort(AttachmentSortColumn.CreatedAt, Descending: false));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(target.Id, result.Items[0].Id);
    }

    // F.I.R.S.T — SoftDelete thiếu file.
    [Fact]
    public async Task ATR05_SoftDeleteAsync_ShouldThrow_WhenMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var sut = new AttachmentRepository(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.SoftDeleteAsync(Guid.NewGuid(), "x"));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
    }
}
