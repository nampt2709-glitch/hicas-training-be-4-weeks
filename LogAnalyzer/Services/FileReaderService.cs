namespace LogAnalyzer; // Không gian tên gom toàn bộ dự án LogAnalyzer.

// Lớp: đọc file theo dòng (stream) để tránh nạp cả file vào RAM một lần.
public sealed class FileReaderService : IFileReader // Triển khai giao diện đọc file.
{
    // Nhiệm vụ: trả về luồng dòng đồng bộ từ đường dẫn. Cách làm: ủy quyền cho File.ReadLines của BCL.
    public IEnumerable<string> ReadLines(string path) => File.ReadLines(path); // Gọi API .NET đọc từng dòng lazy.

    // Nhiệm vụ: trả về luồng dòng bất đồng bộ. Cách làm: ủy quyền File.ReadLinesAsync, truyền token hủy.
    public IAsyncEnumerable<string> ReadLinesAsync(string path, CancellationToken cancellationToken = default) =>
        File.ReadLinesAsync(path, cancellationToken); // Đọc async theo chunk, hỗ trợ CancellationToken.
}
