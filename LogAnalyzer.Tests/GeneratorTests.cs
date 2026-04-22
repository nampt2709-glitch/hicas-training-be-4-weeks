using System.Text;
using LogAnalyzer;
using Xunit;

namespace LogAnalyzer.Tests;

public class GeneratorTests
{
    [Fact]
    public void GT01_ErrorLogGenerator_ShouldCreateLogFile_InLogsFolder()
    {
        ILogGenerator generator = new LogGeneratorService();
        var generatedPath = generator.Generate(
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

    [Fact]
    public void GT02_ErrorCatalog_ShouldContainExactly200Terms()
    {
        Assert.Equal(200, ErrorCatalog.AllTerms.Count);
    }

    [Fact]
    public void GT03_GeneratedFile_ShouldContainKnownErrorTerms()
    {
        ILogGenerator generator = new LogGeneratorService();
        var generatedPath = generator.Generate(
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
