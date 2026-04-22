using System.Text;
using LogAnalyzer;
using Xunit;

namespace LogAnalyzer.Tests;

public class ReaderTests
{
    // Kiểm tra xem FileReader đọc file đồng bộ đúng hay không
    [Fact]
    public void RT01_ReadSync_ShouldReadFileContent()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"loganalyzer_readsync_{Guid.NewGuid():N}.txt");
        var expected = "hello world";

        File.WriteAllText(tempFile, expected, Encoding.UTF8);

        try
        {
            IFileReader reader = new FileReaderService();
            var actual = reader.ReadSync(tempFile);

            Assert.Equal(expected, actual);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    // Kiểm tra xem FileReader đọc file bất đồng bộ đúng hay không
    [Fact]
    public async Task RT02_ReadAsync_ShouldReadFileContent()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"loganalyzer_readasync_{Guid.NewGuid():N}.txt");
        var expected = "hello async world";

        await File.WriteAllTextAsync(tempFile, expected, Encoding.UTF8);

        try
        {
            IFileReader reader = new FileReaderService();
            var actual = await reader.ReadAsync(tempFile);

            Assert.Equal(expected, actual);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}