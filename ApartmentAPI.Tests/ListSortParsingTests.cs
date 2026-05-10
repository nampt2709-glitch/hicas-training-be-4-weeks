using ApartmentAPI;

namespace ApartmentAPI.Tests;

// Kiểm thử ListSortParsing — hướng sort hợp lệ / không hợp lệ và map cột Apartment.
public class ListSortParsingTests
{
    // F.I.R.S.T — sortDir rỗng → không giảm dần.
    [Fact]
    public void LSP01_ParseDescending_ShouldDefaultFalse_WhenNullOrWhitespace()
    {
        Assert.False(ListSortParsing.ParseDescending(null));
        Assert.False(ListSortParsing.ParseDescending("   "));
    }

    // F.I.R.S.T — desc/asc không phân biệt hoa thường.
    [Fact]
    public void LSP02_ParseDescending_ShouldAcceptDescAndAscAliases()
    {
        Assert.True(ListSortParsing.ParseDescending("DESC"));
        Assert.False(ListSortParsing.ParseDescending("ASC"));
    }

    // F.I.R.S.T — sortDir không thuộc whitelist → ApiException 400.
    [Fact]
    public void LSP03_ParseDescending_ShouldThrow_WhenInvalid()
    {
        var ex = Assert.Throws<ApiException>(() => ListSortParsing.ParseDescending("sideways"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.InvalidSortDirection, ex.ErrorCode);
    }

    // F.I.R.S.T — ParseApartmentSort: sort rỗng → CreatedAt + hướng đã parse.
    [Fact]
    public void LSP04_ParseApartmentSort_ShouldDefaultColumn_WhenSortMissing()
    {
        var spec = ListSortParsing.ParseApartmentSort(null, "desc");

        Assert.Equal(ApartmentSortColumn.CreatedAt, spec.Column);
        Assert.True(spec.Descending);
    }

    // F.I.R.S.T — Map room/roomNumber và floor.
    [Fact]
    public void LSP05_ParseApartmentSort_ShouldMapRoomAliases()
    {
        var byRoom = ListSortParsing.ParseApartmentSort("room", null);
        Assert.Equal(ApartmentSortColumn.RoomNumber, byRoom.Column);

        var byFloor = ListSortParsing.ParseApartmentSort("floor", "asc");
        Assert.Equal(ApartmentSortColumn.Floor, byFloor.Column);
    }

    // F.I.R.S.T — Cột Apartment sai → 400.
    [Fact]
    public void LSP06_ParseApartmentSort_ShouldThrow_WhenColumnInvalid()
    {
        var ex = Assert.Throws<ApiException>(() => ListSortParsing.ParseApartmentSort("hackerColumn", null));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ApiErrorCodes.InvalidSortColumn, ex.ErrorCode);
    }

    // F.I.R.S.T — ParseFeedbackSort mặc định.
    [Fact]
    public void LSP07_ParseFeedbackSort_ShouldDefaultCreatedAt()
    {
        var spec = ListSortParsing.ParseFeedbackSort(null, null);

        Assert.Equal(FeedbackSortColumn.CreatedAt, spec.Column);
        Assert.False(spec.Descending);
    }
}
