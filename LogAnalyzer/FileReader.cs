namespace LogAnalyzer;

// <summary>
// Lớp bọc đọc toàn bộ nội dung file vào một chuỗi (một lần đọc).
// Dùng để so sánh hiệu năng: đọc đồng bộ so với bất đồng bộ trong BenchmarkRunner.
// </summary>
public static class FileReader
{
    // Đọc đồng bộ (blocking): thread gọi bị chặn cho đến khi toàn bộ file nạp xong vào RAM.
    public static string ReadSync(string path)
    {
        return File.ReadAllText(path);
    }

    // Đọc bất đồng bộ: trả về Task; thích hợp khi muốn giảm block thread trong lúc I/O (so với nhánh Sync).
    public static Task<string> ReadAsync(string path)
    {
        return File.ReadAllTextAsync(path);
    }
}