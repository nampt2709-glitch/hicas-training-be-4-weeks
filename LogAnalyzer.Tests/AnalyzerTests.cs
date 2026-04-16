using LogAnalyzer;
using Xunit;
using System.Linq;

namespace LogAnalyzer.Tests;

public class AnalyzerTests
{

    [Theory]
    [InlineData("ErrorLog.txt", AnalysisMode.Error)]
    [InlineData("ErrorLog1.log", AnalysisMode.Error)]
    [InlineData("system.log", AnalysisMode.Word)]
    public void AT01_DetectMode_ShouldReturnCorrect(string fileName, AnalysisMode expected)
    {
        var result = Analyzer.DetectMode(fileName);
        Assert.Equal(expected, result);
    }

    // Kiểm tra xem ExtractWords trả về các token đúng hay không
    [Fact]
    public void AT02_ExtractWords_ShouldHandleNormalCase()
    {
        var result = Analyzer.ExtractWords("hello world hello");

        Assert.Equal(3, result.Count());
        Assert.Equal("hello", result.ElementAt(0));
        Assert.Equal("world", result.ElementAt(1));
        Assert.Equal("hello", result.ElementAt(2));
    }

    // Kiểm tra xem ExtractWords trả về empty khi null
    [Fact]
    public void AT03_ExtractWords_ShouldReturnEmpty_WhenNull()
    {
        var result = Analyzer.ExtractWords(null!);
        Assert.Empty(result);
    }

    // Kiểm tra xem ExtractErrorTerms trả về các exception đúng hay không
    [Fact]
    public void AT04ExtractErrorTerms_ShouldDetectErrors()
    {
        var text = "NullReferenceException IOException SqlException";

        var result = Analyzer.ExtractErrorTerms(text).ToList();

        Assert.Contains("NullReferenceException", result);
        Assert.Contains("IOException", result);
        Assert.Contains("SqlException", result);
    }

    // Kiểm tra xem ExtractErrorTerms trả về empty khi không có match
    [Fact]
    public void AT05_ExtractErrorTerms_ShouldBeEmpty_WhenNoMatch()
    {
        var result = Analyzer.ExtractErrorTerms("hello world").ToList();

        Assert.Empty(result);
    }

    // Kiểm tra xem ExtractErrorTerms trả về các exception đúng hay không (không phân biệt hoa thường)
    [Fact]
    public void AT06_ExtractErrorTerms_ShouldBeCaseInsensitive()
    {
        var result = Analyzer.ExtractErrorTerms("nullreferenceexception NULLREFERENCEEXCEPTION").ToList();

        Assert.Equal(2, result.Count);
    }
}