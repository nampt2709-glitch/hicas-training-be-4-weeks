using ApartmentAPI;
using ApartmentAPI.DTOs;

namespace ApartmentAPI.Tests;

// Kiểm thử PaginationQuery — normalize và parse chuỗi query (giới hạn pageSize).
public class PaginationQueryTests
{
    // F.I.R.S.T — page < 1 được nâng lên 1.
    // 3A — Act: Normalize(0, 20); Assert: Page = 1.
    [Fact]
    public void PQ01_Normalize_ShouldClampPageToMinOne()
    {
        var (page, size) = PaginationQuery.Normalize(0, 20);

        Assert.Equal(1, page);
        Assert.Equal(20, size);
    }

    // F.I.R.S.T — pageSize < 1 → default.
    [Fact]
    public void PQ02_Normalize_ShouldUseDefaultPageSize_WhenInvalid()
    {
        var (_, size) = PaginationQuery.Normalize(1, 0);

        Assert.Equal(PaginationQuery.DefaultPageSize, size);
    }

    // F.I.R.S.T — pageSize vượt Max bị cắt.
    [Fact]
    public void PQ03_Normalize_ShouldCapPageSizeAtMax()
    {
        var (_, size) = PaginationQuery.Normalize(1, 9_999);

        Assert.Equal(PaginationQuery.MaxPageSize, size);
    }

    // F.I.R.S.T — ParseFromQuery với pageSize explicit > Max → 400.
    [Fact]
    public void PQ04_ParseFromQuery_ShouldThrow_WhenExplicitPageSizeExceedsMax()
    {
        var ex = Assert.Throws<ApiException>(() =>
            PaginationQuery.ParseFromQuery("1", (PaginationQuery.MaxPageSize + 1).ToString()));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.PageSizeTooLarge, ex.ErrorCode);
    }

    // F.I.R.S.T — ParseFromQuery null → default normalization.
    [Fact]
    public void PQ05_ParseFromQuery_ShouldAcceptNullRawValues()
    {
        var (page, size) = PaginationQuery.ParseFromQuery(null, null);

        Assert.Equal(1, page);
        Assert.Equal(PaginationQuery.DefaultPageSize, size);
    }
}
