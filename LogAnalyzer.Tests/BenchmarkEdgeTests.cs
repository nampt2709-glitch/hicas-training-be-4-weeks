using LogAnalyzer;
using Xunit;

namespace LogAnalyzer.Tests;

public class BenchmarkEdgeTests
{
    // F.I.R.S.T: chạy nhanh với file rỗng, không phụ thuộc external service.
    // 3A — Arrange: tạo file rỗng. Act: chạy benchmark Word. Assert: đọc + đếm với FrequencyItems rỗng.
    [Fact]
    public async Task BET01_RunAsync_ShouldReturnEmptyFrequencyItems_ForEmptyWordFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"loganalyzer_empty_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, string.Empty);

        try
        {
            IBenchmarkRunner runner = new BenchmarkRunnerService(new FileReaderService());
            var report = await runner.RunAsync(tempFile, AnalysisMode.Word);

            Assert.Equal(2, report.ReadRuns.Count);
            Assert.Equal(3, report.CountRuns.Count);
            Assert.Empty(report.FrequencyItems);
            Assert.All(report.ReadRuns, run => Assert.Empty(run.Items));
            Assert.Empty(report.CountRuns[0].Items);
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

    // F.I.R.S.T: độc lập với BET01 vì file khác và mode khác.
    // 3A — Arrange: file rỗng dạng ErrorLog. Act: benchmark Error. Assert: FrequencyItems rỗng và đủ số pha đo.
    [Fact]
    public async Task BET02_RunAsync_ShouldReturnEmptyFrequencyItems_ForEmptyErrorFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"ErrorLog_empty_{Guid.NewGuid():N}.log");
        await File.WriteAllTextAsync(tempFile, string.Empty);

        try
        {
            IBenchmarkRunner runner = new BenchmarkRunnerService(new FileReaderService());
            var report = await runner.RunAsync(tempFile, AnalysisMode.Error);

            Assert.Equal(AnalysisMode.Error, report.Mode);
            Assert.Equal(2, report.ReadRuns.Count);
            Assert.Equal(3, report.CountRuns.Count);
            Assert.Empty(report.FrequencyItems);
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

