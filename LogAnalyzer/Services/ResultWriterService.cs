using System.Text;

namespace LogAnalyzer;

// Ghi báo cáo UTF-8 vào thư mục Results.
public sealed class ResultWriterService : IResultWriter
{
    private const int StringBuilderBaseCapacity = 4_096;

    public string Write(BenchmarkReport report)
    {
        var resultsDir = Path.Combine(AppContext.BaseDirectory, "Results");
        Directory.CreateDirectory(resultsDir);

        var fileName = $"Result_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt";
        var outputPath = Path.Combine(resultsDir, fileName);

        var sb = new StringBuilder(StringBuilderBaseCapacity);

        sb.AppendLine($"Mode: {report.Mode}");
        sb.AppendLine($"Source File: {Path.GetFileName(report.SourceFile)}");
        sb.AppendLine($"Generated At: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine();

        sb.AppendLine("===== PERFORMANCE =====");
        sb.AppendLine($"{"Mode",-30}{"Time(ms)",12}");

        foreach (var run in report.Runs)
        {
            sb.AppendLine($"{run.Label,-30}{run.ElapsedMilliseconds,12}");
        }

        sb.AppendLine();

        if (report.TopItems.Count == 0)
        {
            sb.AppendLine(report.Mode == AnalysisMode.Word
                ? "No words found."
                : "No error-related terms found.");
        }
        else
        {
            sb.AppendLine(report.Mode == AnalysisMode.Word
                ? "===== TOP 50 WORDS ====="
                : "===== TOP 50 ERRORS =====");

            var limit = Math.Min(50, report.TopItems.Count);
            for (var i = 0; i < limit; i++)
            {
                var item = report.TopItems[i];
                sb.AppendLine($"{i + 1,2}. {item.Name} - {item.Count}");
            }
        }

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        return outputPath;
    }
}
