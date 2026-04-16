using LogAnalyzer;
using Xunit;

namespace LogAnalyzer.Tests;

public class CounterTests
{
    // Kiểm tra xem Sequential đếm đúng hay không
    [Fact]
    public void CT01_Sequential_ShouldCountCorrectly()
    {
        var input = new[] { "a", "b", "a", "c", "b", "a" };

        var result = Counter.Sequential(input);

        Assert.Equal(3, result.First(x => x.Name == "a").Count);
        Assert.Equal(2, result.First(x => x.Name == "b").Count);
        Assert.Equal(1, result.First(x => x.Name == "c").Count);
    }

    // Kiểm tra xem ParallelForEach đếm đúng hay không
    [Fact]
    public void CT02_ParallelForEach_ShouldCountCorrectly()
    {
        var input = new[] { "a", "b", "a", "c", "b", "a" };

        var result = Counter.ParallelForEach(input);

        Assert.Equal(3, result.First(x => x.Name == "a").Count);
        Assert.Equal(2, result.First(x => x.Name == "b").Count);
        Assert.Equal(1, result.First(x => x.Name == "c").Count);
    }

    // Kiểm tra xem Plinq đếm đúng hay không
    [Fact]
    public void CT03_Plinq_ShouldCountCorrectly()
    {
        var input = new[] { "a", "b", "a", "c", "b", "a" };

        var result = Counter.Plinq(input);

        Assert.Equal(3, result.First(x => x.Name == "a").Count);
        Assert.Equal(2, result.First(x => x.Name == "b").Count);
        Assert.Equal(1, result.First(x => x.Name == "c").Count);
    }

    // Kiểm tra xem tất cả các hàm đếm đều trả về kết quả giống nhau cho input bình thường
    [Fact]
    public void CT04_AllCounters_ShouldReturnSameResult_ForNormalInput()
    {
        var input = Enumerable.Range(1, 10_000)
            .Select(i => i % 3 == 0 ? "a" : i % 3 == 1 ? "b" : "c")
            .ToList();

        var seq = Counter.Sequential(input);
        var par = Counter.ParallelForEach(input);
        var plinq = Counter.Plinq(input);

        Assert.Equal(ToPairs(seq), ToPairs(par));
        Assert.Equal(ToPairs(seq), ToPairs(plinq));
    }

    // Kiểm tra xem Sequential trả về empty khi input rỗng
    [Fact]
    public void CT05_Sequential_ShouldReturnEmpty_WhenInputEmpty()
    {
        var result = Counter.Sequential(Array.Empty<string>());

        Assert.Empty(result);
    }

    // Kiểm tra xem ParallelForEach trả về empty khi input rỗng
    [Fact]
    public void CT06_ParallelForEach_ShouldReturnEmpty_WhenInputEmpty()
    {
        var result = Counter.ParallelForEach(Array.Empty<string>());

        Assert.Empty(result);
    }

    // Kiểm tra xem Plinq trả về empty khi input rỗng
    [Fact]
    public void CT07_Plinq_ShouldReturnEmpty_WhenInputEmpty()
    {
        var result = Counter.Plinq(Array.Empty<string>());

        Assert.Empty(result);
    }

    // Kiểm tra xem Counter để lại các item rỗng và whitespace
    [Fact]
    public void CT08_Counter_ShouldIgnoreEmptyAndWhitespaceItems()
    {
        var input = new[] { "a", " ", "", "\t", "a" };

        var seq = Counter.Sequential(input);
        var par = Counter.ParallelForEach(input);
        var plinq = Counter.Plinq(input);

        Assert.Equal(2, seq.Single(x => x.Name == "a").Count);
        Assert.Equal(2, par.Single(x => x.Name == "a").Count);
        Assert.Equal(2, plinq.Single(x => x.Name == "a").Count);
    }

    // Kiểm tra xem Counter để lại các item rỗng và whitespace
    [Fact]
    public void CT09_Counter_ShouldHandleSkewedData()
    {
        var input = Enumerable.Repeat("error", 9_999)
            .Concat(new[] { "timeout" })
            .ToList();

        var seq = Counter.Sequential(input);
        var par = Counter.ParallelForEach(input);
        var plinq = Counter.Plinq(input);

        Assert.Equal(9_999, seq.First(x => x.Name == "error").Count);
        Assert.Equal(9_999, par.First(x => x.Name == "error").Count);
        Assert.Equal(9_999, plinq.First(x => x.Name == "error").Count);

        Assert.Equal(1, seq.First(x => x.Name == "timeout").Count);
        Assert.Equal(1, par.First(x => x.Name == "timeout").Count);
        Assert.Equal(1, plinq.First(x => x.Name == "timeout").Count);
    }

    // Kiểm tra xem Counter để lại các item rỗng và whitespace
    [Fact]
    public void CT10_Counter_ShouldHandleHighCardinality()
    {
        var input = Enumerable.Range(1, 2_000)
            .Select(i => $"word{i}")
            .ToList();

        var seq = Counter.Sequential(input);
        var par = Counter.ParallelForEach(input);
        var plinq = Counter.Plinq(input);

        Assert.Equal(2_000, seq.Count);
        Assert.Equal(2_000, par.Count);
        Assert.Equal(2_000, plinq.Count);
    }

    private static List<(string Name, int Count)> ToPairs(IEnumerable<FrequencyItem> items)
    {
        return items
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => (x.Name, x.Count))
            .ToList();
    }
}