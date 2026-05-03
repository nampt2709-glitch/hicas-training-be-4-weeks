using LogAnalyzer;
using Xunit;

namespace LogAnalyzer.Tests;

public class BenchmarkTests
{
    // Kiểm tra BenchmarkRunner: 2 pha đọc và 3 phép đếm cho chế độ Word.
    [Fact]
    public async Task BT01_ShouldRunReadThenCountBenchmarks_ForWordMode()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"loganalyzer_word_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, "hello world hello test test file");

        try
        {
            IBenchmarkRunner runner = new BenchmarkRunnerService(new FileReaderService());
            var report = await runner.RunAsync(tempFile, AnalysisMode.Word);

            Assert.NotNull(report);
            Assert.Equal(AnalysisMode.Word, report.Mode);
            Assert.Equal(tempFile, report.SourceFile);
            Assert.Equal(2, report.ReadRuns.Count);
            Assert.Equal(3, report.CountRuns.Count);
            Assert.True(report.ReadRuns.All(r => r.ElapsedMilliseconds >= 0));
            Assert.True(report.CountRuns.All(r => r.ElapsedMilliseconds >= 0));
            Assert.Equal(6, report.TotalOccurrences);
            Assert.True(report.FrequencyItems.Count >= 4);
            Assert.NotEmpty(report.CountRuns[0].Items);
            Assert.Empty(report.CountRuns[1].Items);
            Assert.Empty(report.CountRuns[2].Items);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    // Kiểm tra BenchmarkRunner cho chế độ Error (danh mục lỗi).
    [Fact]
    public async Task BT02_ShouldRunReadThenCountBenchmarks_ForErrorMode()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"ErrorLog_{Guid.NewGuid():N}.log");
        await File.WriteAllTextAsync(tempFile, "NullReferenceException error exception timeout fail");

        try
        {
            IBenchmarkRunner runner = new BenchmarkRunnerService(new FileReaderService());
            var report = await runner.RunAsync(tempFile, AnalysisMode.Error);

            Assert.NotNull(report);
            Assert.Equal(AnalysisMode.Error, report.Mode);
            Assert.Equal(2, report.ReadRuns.Count);
            Assert.Equal(3, report.CountRuns.Count);
            Assert.True(report.FrequencyItems.Count > 0);
            Assert.Equal(1, report.TotalOccurrences);
            Assert.Equal(1, report.DistinctTypeCount);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
