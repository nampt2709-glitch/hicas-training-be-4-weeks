using System.Text;
using LogAnalyzer;
using Xunit;

namespace LogAnalyzer.Tests;

public class ReaderTests
{
    // F.I.R.S.T: nhanh, độc lập, dễ lặp lại.
    // 3A — Arrange: tạo file nhiều dòng. Act: đọc bằng ReadLines. Assert: nội dung đúng thứ tự.
    [Fact]
    public void RT01_ReadLines_ShouldReadFileContent()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"loganalyzer_readlines_{Guid.NewGuid():N}.txt");
        var expected = new[] { "hello", "world", "stream" };

        File.WriteAllLines(tempFile, expected, Encoding.UTF8);

        try
        {
            IFileReader reader = new FileReaderService();
            var actual = reader.ReadLines(tempFile).ToArray();

            Assert.Equal(expected, actual);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    // F.I.R.S.T: nhanh, không phụ thuộc shared state.
    // 3A — Arrange: tạo file 2 dòng. Act: đọc bằng ReadLinesAsync. Assert: nhận đủ và đúng dữ liệu.
    [Fact]
    public async Task RT02_ReadLinesAsync_ShouldReadFileContent()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"loganalyzer_readlinesasync_{Guid.NewGuid():N}.txt");
        var expected = new[] { "hello async", "world async" };

        await File.WriteAllLinesAsync(tempFile, expected, Encoding.UTF8);

        try
        {
            IFileReader reader = new FileReaderService();
            var actual = new List<string>();
            await foreach (var line in reader.ReadLinesAsync(tempFile))
            {
                actual.Add(line);
            }

            Assert.Equal(expected, actual);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    // F.I.R.S.T: deterministic vì luôn dùng path không tồn tại.
    // 3A — Arrange: path không tồn tại. Act + Assert: ReadLines ném FileNotFoundException.
    [Fact]
    public void RT03_ReadLines_ShouldThrow_WhenFileNotFound()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_missing.txt");
        IFileReader reader = new FileReaderService();

        Assert.Throws<FileNotFoundException>(() => reader.ReadLines(missingPath).ToList());
    }

    // F.I.R.S.T: độc lập và kiểm chứng nhánh async lỗi I/O.
    // 3A — Arrange: path không tồn tại. Act + Assert: ReadLinesAsync ném FileNotFoundException khi enumerate.
    [Fact]
    public async Task RT04_ReadLinesAsync_ShouldThrow_WhenFileNotFound()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_missing_async.txt");
        IFileReader reader = new FileReaderService();

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
        {
            await foreach (var _ in reader.ReadLinesAsync(missingPath))
            {
            }
        });
    }
}