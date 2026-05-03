using System.Text;
using System.Text.RegularExpressions;
using LogAnalyzer;
using Xunit;

namespace LogAnalyzer.Tests;

public class ResultWriterFrequencyListTests
{
    // F.I.R.S.T: dữ liệu cố định, không phụ thuộc random.
    // 3A — Arrange: tạo report Word với 60 item. Act: ghi file kết quả. Assert: ghi đủ 60 dòng tần suất.
    [Fact]
    public void RWFL01_Write_ShouldEmitFullFrequencyList()
    {
        var frequencyItems = Enumerable.Range(1, 60)
            .Select(i => new FrequencyItem($"word{i}", 100 - i))
            .ToList();

        var report = BuildReport(AnalysisMode.Word, "sample.txt", frequencyItems);
        IResultWriter writer = new ResultWriterService();
        var outputPath = writer.Write(report);

        try
        {
            var lines = File.ReadAllLines(outputPath, Encoding.UTF8);
            var freqLines = lines
                .Where(static line => line.Contains(" - ", StringComparison.Ordinal) &&
                                      Regex.IsMatch(line, @"^\s*\d+\."))
                .ToList();

            Assert.Equal(60, freqLines.Count);
            Assert.Contains(lines, line => line.Contains("60.", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.Contains("===== FULL WORD FREQUENCY LIST =====", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    // F.I.R.S.T: kiểm thử nhánh header chế độ Error.
    // 3A — Arrange: report mode Error có dữ liệu tần suất. Act: write. Assert: header FULL ERROR FREQUENCY LIST.
    [Fact]
    public void RWFL02_Write_ShouldUseErrorHeader_WhenModeIsError()
    {
        var report = BuildReport(
            AnalysisMode.Error,
            "ErrorLog_sample.log",
            new List<FrequencyItem> { new("SqlException", 12) });

        IResultWriter writer = new ResultWriterService();
        var outputPath = writer.Write(report);

        try
        {
            var content = File.ReadAllText(outputPath, Encoding.UTF8);
            Assert.Contains("===== FULL ERROR FREQUENCY LIST =====", content);
            Assert.DoesNotContain("===== FULL WORD FREQUENCY LIST =====", content);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    private static BenchmarkReport BuildReport(
        AnalysisMode mode,
        string sourceFile,
        List<FrequencyItem> frequencyItems)
    {
        long totalOccurrences = 0;
        foreach (var item in frequencyItems)
        {
            totalOccurrences += item.Count;
        }

        var distinctTypeCount = frequencyItems.Count;

        var readRuns = new BenchmarkRun[]
        {
            new("Đọc đồng bộ (Sync ReadLines)", 1L, new List<FrequencyItem>()),
            new("Đọc bất đồng bộ (Async ReadLinesAsync)", 1L, new List<FrequencyItem>()),
        };

        var countRuns = new BenchmarkRun[]
        {
            new("Đếm tuần tự (Sequential)", 1L, frequencyItems),
            new("Đếm Parallel.ForEach", 1L, new List<FrequencyItem>()),
            new("Đếm PLINQ", 1L, new List<FrequencyItem>()),
        };

        return new BenchmarkReport(mode, sourceFile, readRuns, countRuns, frequencyItems, totalOccurrences, distinctTypeCount);
    }
}
