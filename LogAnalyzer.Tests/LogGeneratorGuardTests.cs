using LogAnalyzer;
using Xunit;

namespace LogAnalyzer.Tests;

public class LogGeneratorGuardTests
{
    // F.I.R.S.T: nhanh và độc lập, không phát sinh file output vì fail sớm.
    // 3A — Arrange: lineCount = 0. Act + Assert: Generate ném ArgumentOutOfRangeException cho tham số lineCount.
    [Fact]
    public void LGG01_Generate_ShouldThrow_WhenLineCountIsNotPositive()
    {
        ILogGenerator generator = new LogGeneratorService();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(
            lineCount: 0,
            selectedTypeCount: 10));

        Assert.Equal("lineCount", ex.ParamName);
    }

    // F.I.R.S.T: deterministic và dễ đọc.
    // 3A — Arrange: selectedTypeCount = 0. Act + Assert: Generate ném ArgumentOutOfRangeException cho selectedTypeCount.
    [Fact]
    public void LGG02_Generate_ShouldThrow_WhenSelectedTypeCountIsNotPositive()
    {
        ILogGenerator generator = new LogGeneratorService();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(
            lineCount: 10,
            selectedTypeCount: 0));

        Assert.Equal("selectedTypeCount", ex.ParamName);
    }
}
