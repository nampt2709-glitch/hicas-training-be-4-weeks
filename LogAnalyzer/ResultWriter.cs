using System.Text;

namespace LogAnalyzer;

// <summary>
// Xuất báo cáo benchmark ra file .txt: metadata, thời gian 6 lần chạy, và top 50 mục tần suất.
// </summary>
public static class ResultWriter
{
    public static string Write(BenchmarkReport report)
    {
        // Đặt file kết quả cạnh executable, thư mục Results; tạo thư mục nếu chưa có.
        var resultsDir = Path.Combine(AppContext.BaseDirectory, "Results");
        Directory.CreateDirectory(resultsDir);

        // Tên file theo thời gian để mỗi lần chạy không ghi đè kết quả cũ.
        var fileName = $"Result_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt";
        var outputPath = Path.Combine(resultsDir, fileName);

        var sb = new StringBuilder();

        // --- Phần đầu: chế độ phân tích, tên file nguồn, thời điểm xuất báo cáo ---
        sb.AppendLine($"Mode: {report.Mode}");
        sb.AppendLine($"Source File: {Path.GetFileName(report.SourceFile)}");
        sb.AppendLine($"Generated At: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine();

        // --- Bảng hiệu năng: mỗi dòng là một "cách" chạy (Sequential / Parallel / PLINQ × Sync / Async) và thời gian ms ---
        sb.AppendLine("===== PERFORMANCE =====");
        sb.AppendLine($"{"Mode",-30}{"Time(ms)",12}");

        foreach (var run in report.Runs)
        {
            sb.AppendLine($"{run.Label,-30}{run.ElapsedMilliseconds,12}");
        }

        sb.AppendLine();

        // --- Phần tần suất: TopItems lấy từ lần chạy đầu tiên có dữ liệu trong BenchmarkReport; in tối đa 50 dòng ---
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

            var top50 = report.TopItems.Take(50).ToList();

            for (var i = 0; i < top50.Count; i++)
            {
                var item = top50[i];
                sb.AppendLine($"{i + 1,2}. {item.Name} - {item.Count}");
            }
        }

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        return outputPath;
    }
}