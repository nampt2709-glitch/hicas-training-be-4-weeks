namespace LogAnalyzer;

// Đọc toàn bộ nội dung file văn bản (sync/async) phục vụ phân tích.
public interface IFileReader
{
    string ReadSync(string path);
    Task<string> ReadAsync(string path);
}
