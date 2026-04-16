using System.Text;
using LogAnalyzer;
using Xunit;

namespace LogAnalyzer.Tests;

public class GeneratorTests
{
    // Kiểm tra xem ErrorLogGenerator tạo file log đúng hay không
    [Fact]
    public void GT01_ErrorLogGenerator_ShouldCreateLogFile_InLogsFolder()
    {
        var generatedPath = LogGenerator.Generate(
            lineCount: 100,
            selectedTypeCount: 50,
            progress: null);

        try
        {
            Assert.True(File.Exists(generatedPath));
            Assert.Contains("Logs", generatedPath);
            Assert.Contains("ErrorLog_", Path.GetFileName(generatedPath));

            var lines = File.ReadAllLines(generatedPath, Encoding.UTF8);
            Assert.Equal(100, lines.Length);
        }
        finally
        {
            if (File.Exists(generatedPath))
            {
                File.Delete(generatedPath);
            }
        }
    }

    // Kiểm tra xem ErrorCatalog có chứa đúng 200 term hay không
    [Fact]
    public void GT02_ErrorCatalog_ShouldContainExactly200Terms()
    {
        Assert.Equal(200, ErrorCatalog.AllTerms.Count);
    }

    // Kiểm tra xem file do ErrorLogGenerator tạo có thuật ngữ lỗi nhận diện được hay không
    [Fact]
    public void GT03_GeneratedFile_ShouldContainKnownErrorTerms()
    {
        var generatedPath = LogGenerator.Generate(
            lineCount: 200,
            selectedTypeCount: 50,
            progress: null);

        try
        {
            var content = File.ReadAllText(generatedPath, Encoding.UTF8);
            var detected = Analyzer.ExtractErrorTerms(content);

            Assert.True(detected.Count() > 0);
        }
        finally
        {
            if (File.Exists(generatedPath))
            {
                File.Delete(generatedPath);
            }
        }
    }
}