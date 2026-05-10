using System.Text;
using LogAnalyzer;
using Xunit;

namespace LogAnalyzer.Tests;

public class WriterTests
{
    [Fact]
    public void WTT01_ShouldCreateResultFile_InResultsFolder()
    {
        var freqItems = new List<FrequencyItem>
        {
            new("hello", 3),
            new("world", 2),
        };

        var report = BuildSampleReport(AnalysisMode.Word, "sample.txt", freqItems);

        IResultWriter writer = new ResultWriterService();
        var outputPath = writer.Write(report);

        try
        {
            Assert.True(File.Exists(outputPath));
            Assert.Contains("Results", outputPath);
            Assert.Contains("Result_", Path.GetFileName(outputPath));

            var content = File.ReadAllText(outputPath, Encoding.UTF8);

            Assert.Contains("Mode: Word", content);
            Assert.Contains("Source File: sample.txt", content);
            Assert.Contains("Total Words (occurrences): 5", content);
            Assert.Contains("===== READ PERFORMANCE (wall / CPU / RAM) =====", content);
            Assert.Contains("===== COUNT PERFORMANCE (wall / CPU / RAM) =====", content);
            Assert.Contains("===== FULL WORD FREQUENCY LIST =====", content);
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

    [Fact]
    public void WTT02_ShouldWriteNoWordsFound_WhenFrequencyItemsEmpty()
    {
        var report = BuildSampleReport(AnalysisMode.Word, "empty.txt", Array.Empty<FrequencyItem>());

        IResultWriter writer = new ResultWriterService();
        var outputPath = writer.Write(report);

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

    [Fact]
    public void WTT03_ShouldWriteNoErrorTermsFound_WhenFrequencyItemsEmpty()
    {
        var report = BuildSampleReport(AnalysisMode.Error, "empty.log", Array.Empty<FrequencyItem>());

        IResultWriter writer = new ResultWriterService();
        var outputPath = writer.Write(report);

        try
        {
            var content = File.ReadAllText(outputPath, Encoding.UTF8);

            Assert.Contains("Total Errors (occurrences): 0", content);
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

    [Fact]
    public void WTT04_ShouldWriteMachineLineAndResourceColumns_WhenCpuAndWorkingSetProvided()
    {
        var freqItems = new List<FrequencyItem> { new("only", 1) };
        var readRuns = new BenchmarkRun[]
        {
            new("Đọc test CPU/RAM", 11L, new List<FrequencyItem>(), 888_001L, 3L * 1024L * 1024L),
        };
        var countRuns = new BenchmarkRun[]
        {
            new("Đếm test CPU/RAM", 22L, freqItems, 888_002L, 5L * 1024L * 1024L),
            new("Đếm Parallel.ForEach", 1L, new List<FrequencyItem>()),
            new("Đếm PLINQ", 1L, new List<FrequencyItem>()),
        };

        var report = new BenchmarkReport(
            AnalysisMode.Word,
            "resource.txt",
            readRuns,
            countRuns,
            freqItems,
            1,
            1);

        IResultWriter writer = new ResultWriterService();
        var outputPath = writer.Write(report);

        try
        {
            var content = File.ReadAllText(outputPath, Encoding.UTF8);
            Assert.Contains("Machine:", content);
            Assert.Contains("Cores:", content);
            Assert.Contains("888001", content);
            Assert.Contains("888002", content);
            Assert.Contains("3.00", content);
            Assert.Contains("5.00", content);
            Assert.Contains("Notes: CPU = process processor-time delta", content);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    private static BenchmarkReport BuildSampleReport(
        AnalysisMode mode,
        string sourceFile,
        IReadOnlyList<FrequencyItem> frequencyItems)
    {
        var freqList = frequencyItems as List<FrequencyItem> ?? frequencyItems.ToList();

        var readRuns = new BenchmarkRun[]
        {
            new("Đọc đồng bộ (Sync ReadLines)", 3L, new List<FrequencyItem>()),
            new("Đọc bất đồng bộ (Async ReadLinesAsync)", 4L, new List<FrequencyItem>()),
        };

        long totalOccurrences = 0;
        foreach (var item in freqList)
        {
            totalOccurrences += item.Count;
        }

        var distinctTypeCount = freqList.Count;

        var countRuns = new BenchmarkRun[]
        {
            new("Đếm tuần tự (Sequential)", 10L, freqList),
            new("Đếm Parallel.ForEach", 8L, new List<FrequencyItem>()),
            new("Đếm PLINQ", 7L, new List<FrequencyItem>()),
        };

        return new BenchmarkReport(
            mode,
            sourceFile,
            readRuns,
            countRuns,
            freqList,
            totalOccurrences,
            distinctTypeCount);
    }
}
