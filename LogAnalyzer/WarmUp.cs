using System.Collections.Concurrent; // ConcurrentDictionary cho đoạn khởi động thử nghiệm đếm.

namespace LogAnalyzer; // Không gian tên dự án.

// Lớp tĩnh: chạy một lần trước benchmark thật để JIT và cache regex/đường song song ổn định.
public static class WarmUp
{
    private static int _isWarmed; // Cờ Interlocked: 0 = chưa warm-up, 1 = đã chạy xong.

    // Nhiệm vụ: đảm bảo warm-up chỉ một lần trong tiến trình. Cách làm: file tạm + gọi Analyzer/Counter giống benchmark thật.
    public static async Task EnsureWarmedUpAsync(IFileReader fileReader, Action<string>? progress = null)
    {
        if (Interlocked.CompareExchange(ref _isWarmed, 1, 0) != 0) // Thử đặt 1 nếu đang là 0; nếu đã 1 thì thoát.
        {
            return; // Luồng khác hoặc lần trước đã warm-up.
        }

        progress?.Invoke("Warm-up: preparing runtime and analyzers..."); // Thông báo tiến độ tùy chọn.

        var tempPath = Path.Combine(Path.GetTempPath(), $"loganalyzer_warmup_{Guid.NewGuid():N}.txt"); // Đường file tạm duy nhất.
        var lines = new[] // Mảng vài dòng mẫu ngắn.
        {
            "NullReferenceException timeout cache", // Dòng có từ khóa lỗi và từ thường.
            "Hello world benchmark warmup", // Dòng chữ thường cho Word mode.
            "SqlException network retry", // Thêm một loại lỗi catalog.
            "hello HELLO world", // Kiểm tra chữ hoa/thường.
        };

        await File.WriteAllLinesAsync(tempPath, lines).ConfigureAwait(false); // Ghi file tạm không bắt sync context.

        try // Khối bảo đảm xóa file dù lỗi.
        {
            _ = Analyzer.DetectMode("ErrorLog_warmup.log"); // Gọi DetectMode để JIT nhánh Error.

            foreach (var line in fileReader.ReadLines(tempPath)) // Duyệt sync từng dòng warm-up.
            {
                _ = Analyzer.MaterializeTokens(line, AnalysisMode.Word); // JIT đường Materialize Word.
                _ = Analyzer.MaterializeTokens(line, AnalysisMode.Error); // JIT đường Materialize Error.
            }

            await foreach (var line in fileReader.ReadLinesAsync(tempPath).ConfigureAwait(false)) // Duyệt async warm-up.
            {
                _ = Analyzer.ExtractWords(line).ToList(); // JIT iterator Word.
                _ = Analyzer.ExtractErrorTerms(line).ToList(); // JIT iterator Error.
            }

            var seedTokens = new[] { "hello", "world", "hello", "SqlException", "SqlException" }; // Token mẫu cho Counter.
            _ = Counter.Sequential(seedTokens); // Warm-up đếm tuần tự.
            _ = Counter.ParallelForEach(seedTokens); // Warm-up Parallel.ForEach.
            _ = Counter.Plinq(seedTokens); // Warm-up PLINQ.

            var concurrent = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase); // Từ điển song song mẫu.
            foreach (var token in seedTokens) // Lặp token mẫu.
            {
                concurrent.AddOrUpdate(token, 1, static (_, oldValue) => oldValue + 1); // Cộng dồn an toàn luồng.
            }

            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // Từ điển thường mẫu.
            foreach (var token in seedTokens) // Lặp lại token.
            {
                if (map.TryGetValue(token, out var current)) // Đã có khóa.
                {
                    map[token] = current + 1; // Tăng bộ đếm.
                }
                else // Chưa có khóa.
                {
                    map[token] = 1; // Khởi tạo 1.
                }
            }

            progress?.Invoke("Warm-up: completed."); // Báo hoàn tất.
        }
        finally // Luôn dọn dẹp file tạm.
        {
            if (File.Exists(tempPath)) // Tránh lỗi nếu file chưa tạo được.
            {
                File.Delete(tempPath); // Xóa khỏi đĩa.
            }
        }
    }
}
