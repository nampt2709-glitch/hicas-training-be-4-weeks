using System.Text; // Encoding.UTF8 cho StreamWriter.

namespace LogAnalyzer; // Không gian tên dự án.

// Lớp: sinh file log lỗi mẫu kích thước lớn để benchmark.
public sealed class LogGeneratorService : ILogGenerator // Triển khai sinh log.
{
    // Nhiệm vụ: ghi file ErrorLog_*.log với lineCount dòng. Cách làm: stream ghi, mỗi dòng random từ catalog + metadata.
    public string Generate(
        int lineCount = 1_000_000, // Số dòng mục tiêu (mặc định 1 triệu).
        int selectedTypeCount = 100, // Số loại lỗi rút từ 200 thuật ngữ.
        Action<string>? progress = null) // Callback tiến độ tùy chọn.
    {
        if (lineCount <= 0) // Tham số số dòng không hợp lệ.
        {
            throw new ArgumentOutOfRangeException(nameof(lineCount)); // Ném lỗi rõ tên tham số.
        }

        if (selectedTypeCount <= 0) // Phải chọn ít nhất một loại lỗi.
        {
            throw new ArgumentOutOfRangeException(nameof(selectedTypeCount)); // Ném lỗi.
        }

        var logsDir = Path.Combine(AppContext.BaseDirectory, "Logs"); // Thư mục Logs cạnh assembly.
        Directory.CreateDirectory(logsDir); // Đảm bảo thư mục tồn tại.

        var fileName = $"ErrorLog_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log"; // Tên file chứa ErrorLog để DetectMode ra Error.
        var outputPath = Path.Combine(logsDir, fileName); // Đường dẫn đầy đủ.

        var random = Random.Shared; // Bộ sinh số ngẫu nhiên luồng an toàn.
        var selectedTypes = ErrorCatalog.GetRandomSample(selectedTypeCount, random); // Mảng loại lỗi dùng lặp lại.

        var modules = new[] // Danh sách module giả lập trong log.
        {
            "Auth", "Api", "Db", "Cache", "UI", // Năm module đầu.
            "Worker", "Gateway", "Scheduler", "Storage", "Report" // Năm module sau.
        };

        var baseTime = DateTime.UtcNow; // Mốc thời gian UTC cho timestamp dòng.
        var checkpoint = Math.Max(1, lineCount / 20); // Báo progress mỗi ~5% số dòng (tối thiểu 1).

        using var stream = new FileStream( // Luồng file có using để đóng tự động.
            outputPath, // Đường file đích.
            FileMode.Create, // Ghi đè / tạo mới.
            FileAccess.Write, // Chỉ ghi.
            FileShare.Read, // Cho phép đọc đồng thời (benchmark đọc).
            bufferSize: 1 << 20); // Bộ đệm 1 MB giảm syscall.

        using var writer = new StreamWriter(stream, Encoding.UTF8); // Ghi văn bản UTF-8 lên stream.

        progress?.Invoke($"Writing to: {outputPath}"); // Báo đường file đang ghi.

        for (var i = 0; i < lineCount; i++) // Vòng lặp từng dòng log.
        {
            var timestamp = baseTime.AddMilliseconds(i).ToString("yyyyMMdd HH:mm:ss.fff"); // Timestamp tăng dần theo chỉ số dòng.
            var errorType = selectedTypes[random.Next(selectedTypes.Length)]; // Chọn ngẫu nhiên một loại lỗi đã rút.
            var module = modules[random.Next(modules.Length)]; // Chọn module ngẫu nhiên.
            var code = random.Next(1000, 9999); // Mã số 4 chữ số giả.

            writer.Write(timestamp); // Ghi phần thời gian.
            writer.Write(" [ERROR] "); // Ghi nhãn mức lỗi.
            writer.Write(errorType); // Ghi tên loại lỗi (trùng catalog).
            writer.Write(" Module="); // Ghi nhãn trường module.
            writer.Write(module); // Ghi tên module.
            writer.Write(" Code="); // Ghi nhãn mã.
            writer.Write(code); // Ghi số mã.
            writer.WriteLine(); // Xuống dòng kết thúc record.

            if ((i + 1) % checkpoint == 0 || i == lineCount - 1) // Đến mốc báo cáo hoặc dòng cuối.
            {
                progress?.Invoke($"Generated {i + 1:n0}/{lineCount:n0} lines..."); // Báo tiến độ.
            }
        }

        return outputPath; // Trả đường dẫn file đã ghi xong.
    }
}
