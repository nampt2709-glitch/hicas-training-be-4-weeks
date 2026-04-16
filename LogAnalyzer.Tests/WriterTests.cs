using System.Text;
using LogAnalyzer;
using Xunit;

namespace LogAnalyzer.Tests;

public class WriterTests
{
    // Kiểm tra xem ResultWriter tạo file kết quả đúng hay không
    [Fact]
    public void WTT01_ShouldCreateResultFile_InResultsFolder()
    {
        var report = new BenchmarkReport(
            AnalysisMode.Word,
            "sample.txt",
            new[]
            {
                new BenchmarkRun("Sync + Sequential", 10, new List<FrequencyItem> { new("hello", 3) }),
                new BenchmarkRun("Sync + Parallel.ForEach", 8, new List<FrequencyItem> { new("hello", 3) }),
                new BenchmarkRun("Sync + PLINQ", 7, new List<FrequencyItem> { new("hello", 3) }),
                new BenchmarkRun("Async + Sequential", 9, new List<FrequencyItem> { new("hello", 3) }),
                new BenchmarkRun("Async + Parallel.ForEach", 6, new List<FrequencyItem> { new("hello", 3) }),
                new BenchmarkRun("Async + PLINQ", 5, new List<FrequencyItem> { new("hello", 3) }),
            },
            new List<FrequencyItem>
            {
                new("hello", 3),
                new("world", 2)
            });

        var outputPath = ResultWriter.Write(report);

        try
        {
            Assert.True(File.Exists(outputPath));
            Assert.Contains("Results", outputPath);
            Assert.Contains("Result_", Path.GetFileName(outputPath));

            var content = File.ReadAllText(outputPath, Encoding.UTF8);

            Assert.Contains("Mode: Word", content);
            Assert.Contains("Source File: sample.txt", content);
            Assert.Contains("===== PERFORMANCE =====", content);
            Assert.Contains("===== TOP 50 WORDS =====", content);
            Assert.Contains("hello - 3", content);
            Assert.Contains("world - 2", content);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    // Kiểm tra xem ResultWriter viết "No words found." khi không có từ
    [Fact]
    public void WTT02_ShouldWriteNoWordsFound_WhenTopItemsEmpty()
    {
        var report = new BenchmarkReport(
            AnalysisMode.Word,
            "empty.txt",
            new[]
            {
                new BenchmarkRun("Sync + Sequential", 1, new List<FrequencyItem>()),
                new BenchmarkRun("Sync + Parallel.ForEach", 1, new List<FrequencyItem>()),
                new BenchmarkRun("Sync + PLINQ", 1, new List<FrequencyItem>()),
                new BenchmarkRun("Async + Sequential", 1, new List<FrequencyItem>()),
                new BenchmarkRun("Async + Parallel.ForEach", 1, new List<FrequencyItem>()),
                new BenchmarkRun("Async + PLINQ", 1, new List<FrequencyItem>()),
            },
            Array.Empty<FrequencyItem>());

        var outputPath = ResultWriter.Write(report);

        try
        {
            var content = File.ReadAllText(outputPath, Encoding.UTF8);

            Assert.Contains("No words found.", content);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    // Kiểm tra xem ResultWriter viết "No error-related terms found." khi không có exception
    [Fact]
    public void WTT03_ShouldWriteNoErrorTermsFound_WhenTopItemsEmpty()
    {
        var report = new BenchmarkReport(
            AnalysisMode.Error,
            "empty.log",
            new[]
            {
                new BenchmarkRun("Sync + Sequential", 1, new List<FrequencyItem>()),
                new BenchmarkRun("Sync + Parallel.ForEach", 1, new List<FrequencyItem>()),
                new BenchmarkRun("Sync + PLINQ", 1, new List<FrequencyItem>()),
                new BenchmarkRun("Async + Sequential", 1, new List<FrequencyItem>()),
                new BenchmarkRun("Async + Parallel.ForEach", 1, new List<FrequencyItem>()),
                new BenchmarkRun("Async + PLINQ", 1, new List<FrequencyItem>()),
            },
            Array.Empty<FrequencyItem>());

        var outputPath = ResultWriter.Write(report);

        try
        {
            var content = File.ReadAllText(outputPath, Encoding.UTF8);

            Assert.Contains("No error-related terms found.", content);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }
}