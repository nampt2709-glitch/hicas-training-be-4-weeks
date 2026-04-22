namespace LogAnalyzer;

// Triển khai đọc file: ánh xạ trực tiếp tới API .NET.
public sealed class FileReaderService : IFileReader
{
    public string ReadSync(string path) => File.ReadAllText(path);

    public Task<string> ReadAsync(string path) => File.ReadAllTextAsync(path);
}
