using System.Text;

namespace LogAnalyzer;

// <summary>
// Sinh file log giả có định dạng cố định để stress-test bộ phân tích.
// Thuật toán: lặp lineCount lần; mỗi dòng chọn ngẫu nhiên một trong selectedTypeCount thuật ngữ lỗi
// (đã lấy mẫu từ catalog 200), ghép thêm module và mã số — không phải log thật từ hệ thống.
// </summary>
public static class LogGenerator
{
    public static string Generate(
        int lineCount = 1_000_000,
        int selectedTypeCount = 100,
        Action<string>? progress = null)
    {
        // Ràng buộc tham số: số dòng và số loại lỗi dùng trong file phải dương.
        if (lineCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineCount));
        }

        if (selectedTypeCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedTypeCount));
        }

        // Thư mục output cạnh executable; tên file bắt đầu bằng "ErrorLog" để DetectMode tự chọn chế độ Error.
        var logsDir = Path.Combine(AppContext.BaseDirectory, "Logs");
        Directory.CreateDirectory(logsDir);

        var fileName = $"ErrorLog_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log";
        var outputPath = Path.Combine(logsDir, fileName);

        var random = Random.Shared;
        // Chọn ngẫu nhiên đúng selectedTypeCount kiểu lỗi khác nhau từ 200 term (Fisher–Yates trong GetRandomSample).
        var selectedTypes = ErrorCatalog.GetRandomSample(selectedTypeCount, random);

        // Danh sách module cố định — chỉ để text log giống log thật, không ảnh hưởng thuật toán đếm lỗi.
        var modules = new[]
        {
            "Auth", "Api", "Db", "Cache", "UI",
            "Worker", "Gateway", "Scheduler", "Storage", "Report"
        };

        // Timestamp tăng dần theo số dòng (mỗi dòng +1ms) để dòng có thời gian hợp lý.
        var baseTime = DateTime.UtcNow;
        // Báo tiến độ khoảng mỗi 5% tổng số dòng (tránh gọi progress mỗi dòng).
        var checkpoint = Math.Max(1, lineCount / 20); // about every 5%

        // FileStream buffer 1MB giảm số lần ghi xuống đĩa khi ghi hàng triệu dòng.
        using var stream = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 1 << 20);

        using var writer = new StreamWriter(stream, Encoding.UTF8);

        progress?.Invoke($"Writing to: {outputPath}");

        // Vòng chính: mỗi lần lặp = một dòng log; token lỗi (errorType) nằm trong catalog nên ExtractErrorTerms nhận diện được.
        for (var i = 0; i < lineCount; i++)
        {
            var timestamp = baseTime.AddMilliseconds(i).ToString("yyyyMMdd HH:mm:ss.fff");
            var errorType = selectedTypes[random.Next(selectedTypes.Length)];
            var module = modules[random.Next(modules.Length)];
            var code = random.Next(1000, 9999);

            writer.Write(timestamp);
            writer.Write(" [ERROR] ");
            writer.Write(errorType);
            writer.Write(" Module=");
            writer.Write(module);
            writer.Write(" Code=");
            writer.Write(code);
            writer.WriteLine();

            if ((i + 1) % checkpoint == 0 || i == lineCount - 1)
            {
                progress?.Invoke($"Generated {i + 1:n0}/{lineCount:n0} lines...");
            }
        }

        return outputPath;
    }
}