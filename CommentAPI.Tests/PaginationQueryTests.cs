using CommentAPI;
using CommentAPI.DTOs;

namespace CommentAPI.Tests;

public class PaginationQueryTests
{
    // F.I.R.S.T: thuần hàm tĩnh, cực nhanh.
    // 3A — Arrange: page = 0. Act: Normalize. Assert: page thành 1 (biên dưới).
    [Fact]
    public void PQ01_Normalize_PageZero_ShouldClampToOne()
    {
        var (page, size) = PaginationQuery.Normalize(0, 10);
        Assert.Equal(1, page);
        Assert.Equal(10, size);
    }

    // F.I.R.S.T: độc lập.
    // 3A — Arrange: page âm. Act: Normalize. Assert: page = 1.
    [Fact]
    public void PQ02_Normalize_PageNegative_ShouldClampToOne()
    {
        var (page, _) = PaginationQuery.Normalize(-100, 5);
        Assert.Equal(1, page);
    }

    // F.I.R.S.T: biên pageSize.
    // 3A — Arrange: pageSize < 1. Act: Normalize. Assert: dùng DefaultPageSize (20).
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PQ03_Normalize_PageSizeNonPositive_ShouldUseDefault(int badSize)
    {
        var (_, size) = PaginationQuery.Normalize(1, badSize);
        Assert.Equal(PaginationQuery.DefaultPageSize, size);
    }

    // F.I.R.S.T: biên trần MaxPageSize.
    // 3A — Arrange: pageSize vượt Max. Act: Normalize. Assert: cắt về MaxPageSize (500).
    [Fact]
    public void PQ04_Normalize_PageSizeAboveMax_ShouldClampToMax()
    {
        var (_, size) = PaginationQuery.Normalize(1, 10_000);
        Assert.Equal(PaginationQuery.MaxPageSize, size);
    }

    // F.I.R.S.T: pageSize đúng bằng Max vẫn giữ.
    // 3A — Arrange: đúng Max. Act: Normalize. Assert: MaxPageSize.
    [Fact]
    public void PQ05_Normalize_PageSizeExactlyMax_ShouldStay()
    {
        var (_, size) = PaginationQuery.Normalize(1, PaginationQuery.MaxPageSize);
        Assert.Equal(PaginationQuery.MaxPageSize, size);
    }

    // F.I.R.S.T: ParseFromQuery với chuỗi rác.
    // 3A — Arrange: page không phải số. Act: ParseFromQuery. Assert: fallback 1 và DefaultPageSize rồi Normalize.
    [Theory]
    [InlineData("abc", "xyz", 1, 20)]
    [InlineData("", "", 1, 20)]
    [InlineData("  ", "  ", 1, 20)]
    public void PQ06_ParseFromQuery_InvalidStrings_ShouldFallback(string? p, string? s, int expPage, int expSize)
    {
        var (page, size) = PaginationQuery.ParseFromQuery(p, s);
        Assert.Equal(expPage, page);
        Assert.Equal(expSize, size);
    }

    // F.I.R.S.T: chuỗi hợp lệ invariant.
    // 3A — Arrange: "2", "15". Act: Parse. Assert: (2,15) sau normalize vẫn 2 và 15.
    [Fact]
    public void PQ07_ParseFromQuery_ValidStrings_ShouldParseInvariant()
    {
        var (page, size) = PaginationQuery.ParseFromQuery("2", "15");
        Assert.Equal(2, page);
        Assert.Equal(15, size);
    }

    // F.I.R.S.T: khoảng trắng quanh số.
    // 3A — Arrange: " 3 ", " 50 ". Act: Parse. Assert: 3 và 50 (50 < Max).
    [Fact]
    public void PQ08_ParseFromQuery_TrimsWhitespace()
    {
        var (page, size) = PaginationQuery.ParseFromQuery(" 3 ", " 50 ");
        Assert.Equal(3, page);
        Assert.Equal(50, size);
    }

    // F.I.R.S.T: query pageSize vượt Max → ApiException 400 (không im lặng Normalize).
    // 3A — Arrange: pageSize = Max+1 trong query. Act: ParseFromQuery. Assert: throw, đúng mã lỗi.
    [Fact]
    public void PQ11_ParseFromQuery_PageSizeAboveMax_ShouldThrowApiException()
    {
        var ex = Assert.Throws<ApiException>(() =>
            PaginationQuery.ParseFromQuery("1", (PaginationQuery.MaxPageSize + 1).ToString()));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.PageSizeTooLarge, ex.ErrorCode);
    }

    // F.I.R.S.T: pageSize đúng bằng Max trong query vẫn chấp nhận.
    // 3A — Arrange: MaxPageSize. Act: Parse. Assert: giữ nguyên sau Normalize.
    [Fact]
    public void PQ12_ParseFromQuery_PageSizeExactlyMax_ShouldSucceed()
    {
        var m = PaginationQuery.MaxPageSize;
        var (page, size) = PaginationQuery.ParseFromQuery("1", m.ToString());
        Assert.Equal(1, page);
        Assert.Equal(m, size);
    }

    // F.I.R.S.T: độ chặt assert — nếu kỳ vọng TotalPages sai (ví dụ 2 thay vì 3) thì test fail.
    // 3A — Arrange: TotalCount=5, PageSize=2. Act: đọc TotalPages. Assert: ceil(5/2)=3; không được ghi 2.
    [Fact]
    public void PQ09_PagedResult_TotalPages_ShouldMatchCeilingFormula()
    {
        var r = new PagedResult<int>
        {
            Items = new List<int>(),
            Page = 1,
            PageSize = 2,
            TotalCount = 5
        };

        Assert.Equal(3, r.TotalPages);
        Assert.NotEqual(2, r.TotalPages);
    }

    // F.I.R.S.T: biên PageSize = 0 tránh chia cho 0.
    // 3A — Arrange: PageSize 0. Act: TotalPages. Assert: 0 theo triển khai PagedResult.
    [Fact]
    public void PQ10_PagedResult_TotalPages_ShouldBeZero_WhenPageSizeZero()
    {
        var r = new PagedResult<int>
        {
            Items = new List<int>(),
            Page = 1,
            PageSize = 0,
            TotalCount = 100
        };

        Assert.Equal(0, r.TotalPages);
    }
}
