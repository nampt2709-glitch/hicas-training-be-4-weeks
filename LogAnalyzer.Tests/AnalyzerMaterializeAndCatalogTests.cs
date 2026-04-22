using LogAnalyzer;
using Xunit;

namespace LogAnalyzer.Tests;

// Bổ sung biên và MaterializeTokens / ErrorCatalog so với AnalyzerTests gốc.
public class AnalyzerMaterializeAndCatalogTests
{
    // F.I.R.S.T: nhanh, không file I/O.
    // 3A — Arrange: chuỗi chỉ khoảng trắng. Act: MaterializeTokens Word. Assert: list rỗng.
    [Theory]
    [InlineData("   ")]
    [InlineData("\n\t\r")]
    public void AM01_MaterializeTokens_ShouldReturnEmpty_WhenWhitespaceOnly(string text)
    {
        var list = Analyzer.MaterializeTokens(text, AnalysisMode.Word);
        Assert.Empty(list);
    }

    // F.I.R.S.T: độc lập.
    // 3A — Arrange: null!. Act: MaterializeTokens. Assert: rỗng (null coi như không hợp lệ → empty list).
    [Fact]
    public void AM02_MaterializeTokens_ShouldReturnEmpty_WhenNull()
    {
        var list = Analyzer.MaterializeTokens(null!, AnalysisMode.Word);
        Assert.Empty(list);
    }

    // F.I.R.S.T: chế độ Word → lowercase invariant.
    // 3A — Arrange: "Hello WORLD". Act: MaterializeTokens Word. Assert: hello, world.
    [Fact]
    public void AM03_MaterializeTokens_WordMode_ShouldLowercaseTokens()
    {
        var list = Analyzer.MaterializeTokens("Hello WORLD", AnalysisMode.Word);
        Assert.Equal(new[] { "hello", "world" }, list);
    }

    // F.I.R.S.T: chế độ Error chỉ giữ token có trong catalog.
    // 3A — Arrange: SqlException và noise. Act: MaterializeTokens Error. Assert: chỉ canonical SqlException.
    [Fact]
    public void AM04_MaterializeTokens_ErrorMode_ShouldFilterByCatalog()
    {
        var list = Analyzer.MaterializeTokens("noise SqlException more", AnalysisMode.Error);
        Assert.Single(list);
        Assert.Equal("SqlException", list[0]);
    }

    // F.I.R.S.T: biên tên file không chứa ErrorLog.
    // 3A — Arrange: tên không có substring. Act: DetectMode. Assert: Word (không phải Error).
    [Theory]
    [InlineData("app.log")]
    [InlineData("ERROR_trace.log")]
    [InlineData("")]
    public void AM05_DetectMode_ShouldReturnWord_WhenFileNameHasNoErrorLogSubstring(string name)
    {
        var mode = Analyzer.DetectMode(name);
        Assert.Equal(AnalysisMode.Word, mode);
    }

    // F.I.R.S.T: phân biệt ErrorLog (không phân hoa thường).
    // 3A — Arrange: chứa "errorlog". Act: DetectMode. Assert: Error.
    [Theory]
    [InlineData("prefix_ErrorLog_suffix.txt")]
    [InlineData("errorlog.txt")]
    public void AM06_DetectMode_ShouldReturnError_WhenContainsErrorLogInsensitive(string name)
    {
        Assert.Equal(AnalysisMode.Error, Analyzer.DetectMode(name));
    }

    // F.I.R.S.T: ExtractErrorTerms không khớp catalog → rỗng.
    // 3A — Arrange: từ không phải exception catalog. Act: ExtractErrorTerms. Assert: empty (kỳ vọng sai cố ý như Assert.NotEmpty sẽ fail).
    [Fact]
    public void AM07_ExtractErrorTerms_ShouldBeEmpty_WhenNoCatalogMatch()
    {
        var result = Analyzer.ExtractErrorTerms("totally_unknown_token_xyz").ToList();
        Assert.Empty(result);
    }

    // F.I.R.S.T: GetRandomSample biên count = 0.
    // 3A — Arrange: count 0. Act: GetRandomSample. Assert: mảng rỗng.
    [Fact]
    public void AM08_ErrorCatalog_GetRandomSample_ShouldReturnEmpty_WhenCountZero()
    {
        var sample = ErrorCatalog.GetRandomSample(0);
        Assert.Empty(sample);
    }

    // F.I.R.S.T: count lớn hơn pool → cắt về độ dài catalog (200).
    // 3A — Arrange: count int.MaxValue. Act: GetRandomSample. Assert: độ dài 200, không vượt.
    [Fact]
    public void AM09_ErrorCatalog_GetRandomSample_ShouldCapAtCatalogSize()
    {
        var sample = ErrorCatalog.GetRandomSample(int.MaxValue);
        Assert.Equal(ErrorCatalog.AllTerms.Count, sample.Length);
        Assert.Equal(200, sample.Length);
    }

    // F.I.R.S.T: đếm cố định 200 term — nếu sai số thì nội bộ BuildAllTerms ném (test fail toàn bộ).
    // 3A — Arrange: không. Act: đọc AllTerms.Count. Assert: 200; NotEqual chứng minh assert chặt nếu đổi kỳ vọng sai.
    [Fact]
    public void AM10_ErrorCatalog_AllTerms_ShouldHaveExactly200Entries()
    {
        Assert.Equal(200, ErrorCatalog.AllTerms.Count);
        Assert.NotEqual(199, ErrorCatalog.AllTerms.Count);
    }
}
