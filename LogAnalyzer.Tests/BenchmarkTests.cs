using LogAnalyzer;
using Xunit;

namespace LogAnalyzer.Tests;

public class BenchmarkTests
{
    // Kiểm tra xem BenchmarkRunner chạy đúng hay không cho mode Word
    [Fact]
    public async Task BT01_ShouldRunAllSixModes_ForWordMode()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"loganalyzer_word_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, "hello world hello test test file");

        try
        {
            var report = await BenchmarkRunner.RunAsync(tempFile, AnalysisMode.Word);

            Assert.NotNull(report);
            Assert.Equal(AnalysisMode.Word, report.Mode);
            Assert.Equal(tempFile, report.SourceFile);
            Assert.Equal(6, report.Runs.Count);
            Assert.True(report.Runs.All(r => r.ElapsedMilliseconds >= 0));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    // Kiểm tra xem BenchmarkRunner chạy đúng hay không cho mode Error
    [Fact]
    public async Task BT02_ShouldRunAllSixModes_ForErrorMode()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"ErrorLog_{Guid.NewGuid():N}.log");
        await File.WriteAllTextAsync(tempFile, "NullReferenceException error exception timeout fail");

        try
        {
            var report = await BenchmarkRunner.RunAsync(tempFile, AnalysisMode.Error);

            Assert.NotNull(report);
            Assert.Equal(AnalysisMode.Error, report.Mode);
            Assert.Equal(6, report.Runs.Count);
            Assert.True(report.TopItems.Count > 0);
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
