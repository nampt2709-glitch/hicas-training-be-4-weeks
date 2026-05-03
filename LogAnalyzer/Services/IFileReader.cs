namespace LogAnalyzer;

// Đọc file theo dạng stream từng dòng để tránh tải toàn bộ file lớn vào RAM.
public interface IFileReader
{
    IEnumerable<string> ReadLines(string path);
    IAsyncEnumerable<string> ReadLinesAsync(string path, CancellationToken cancellationToken = default);
}
