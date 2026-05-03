using System.Text; // StringBuilder và Encoding.UTF8.

namespace LogAnalyzer; // Không gian tên dự án.

// Lớp: ghi BenchmarkReport ra file .txt UTF-8 trong thư mục Results.
public sealed class ResultWriterService : IResultWriter // Triển khai ghi kết quả.
{
    private const int StringBuilderBaseCapacity = 4_096; // Dung tích ban đầu StringBuilder để giảm realocation.

    // Nhiệm vụ: tạo file kết quả và trả về đường dẫn đầy đủ. Cách làm: nối nội dung vào StringBuilder rồi WriteAllText.
    public string Write(BenchmarkReport report)
    {
        var resultsDir = Path.Combine(AppContext.BaseDirectory, "Results"); // Thư mục cạnh assembly.
        Directory.CreateDirectory(resultsDir); // Tạo thư mục nếu chưa tồn tại.

        var fileName = $"Result_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt"; // Tên file theo thời điểm (tránh trùng).
        var outputPath = Path.Combine(resultsDir, fileName); // Đường dẫn đầy đủ file đích.

        var sb = new StringBuilder(StringBuilderBaseCapacity); // Bộ đệm nội dung báo cáo.

        sb.AppendLine($"Mode: {report.Mode}"); // Ghi chế độ Word/Error.
        sb.AppendLine($"Source File: {Path.GetFileName(report.SourceFile)}"); // Chỉ tên file nguồn (ngắn gọn).
        sb.AppendLine($"Generated At: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}"); // Thời điểm ghi file.

        if (report.Mode == AnalysisMode.Word) // Nhánh thống kê từ.
        {
            sb.AppendLine($"Total Words (occurrences): {report.TotalOccurrences:n0}"); // Tổng lần xuất hiện từ.
            sb.AppendLine($"Distinct Word Types: {report.DistinctTypeCount:n0}"); // Số loại từ khác nhau.
        }
        else // Nhánh thống kê lỗi.
        {
            sb.AppendLine($"Total Errors (occurrences): {report.TotalOccurrences:n0}"); // Tổng lần lỗi đếm được.
            sb.AppendLine($"Distinct Error Types: {report.DistinctTypeCount:n0}"); // Số loại lỗi khác nhau.
        }

        sb.AppendLine(); // Dòng trống phân cách.

        sb.AppendLine("===== READ PERFORMANCE (ms) ====="); // Tiêu đề khối đọc file.
        sb.AppendLine($"{"Operation",-45}{"Time(ms)",15}"); // Hàng header hai cột.

        foreach (var run in report.ReadRuns) // Duyệt từng pha đọc Sync/Async.
        {
            sb.AppendLine($"{run.Label,-45}{run.ElapsedMilliseconds,15}"); // Một dòng: nhãn + thời gian.
        }

        sb.AppendLine(); // Dòng trống.

        sb.AppendLine("===== COUNT PERFORMANCE (ms) ====="); // Tiêu đề khối đếm.
        sb.AppendLine($"{"Method",-45}{"Time(ms)",15}"); // Header.

        foreach (var run in report.CountRuns) // Duyệt Sequential / ForEach / PLINQ.
        {
            sb.AppendLine($"{run.Label,-45}{run.ElapsedMilliseconds,15}"); // Nhãn + ms.
        }

        sb.AppendLine(); // Dòng trống.

        if (report.FrequencyItems.Count == 0) // Không có dữ liệu tần suất.
        {
            sb.AppendLine(report.Mode == AnalysisMode.Word
                ? "No words found." // Thông báo Word rỗng.
                : "No error-related terms found."); // Thông báo Error rỗng.
        }
        else // Có danh sách tần suất.
        {
            sb.AppendLine(report.Mode == AnalysisMode.Word
                ? "===== FULL WORD FREQUENCY LIST =====" // Tiêu đề danh sách từ.
                : "===== FULL ERROR FREQUENCY LIST ====="); // Tiêu đề danh sách lỗi.

            for (var i = 0; i < report.FrequencyItems.Count; i++) // Duyệt chỉ số 0..n-1.
            {
                var item = report.FrequencyItems[i]; // Lấy cặp Name/Count.
                sb.AppendLine($"{i + 1,4}. {item.Name} - {item.Count}"); // Dòng STT, tên, tần suất.
            }
        }

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8); // Ghi toàn bộ nội dung UTF-8 không BOM mặc định WriteAllText.
        return outputPath; // Trả đường dẫn cho caller (ví dụ in console).
    }
}
