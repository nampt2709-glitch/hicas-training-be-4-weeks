using System.Reflection; // SetValue cờ tĩnh WarmUp giữa các case.
using LogAnalyzer;
using Xunit;

namespace LogAnalyzer.Tests;

// Kiểm tra WarmUp: JIT một lần, lần sau không gọi lại reader (tránh test lệ thuộc thứ tự nhờ CollectionBehavior tắt song song).
public class WarmUpTests
{
    // Đưa cờ nội bộ về 0 để mỗi test tự quyết “lần đầu warm” — chỉ dùng trong project test.
    private static void ResetWarmUpFlag()
    {
        var field = typeof(WarmUp).GetField("_isWarmed", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        field!.SetValue(null, 0);
    }

    // F.I.R.S.T — xác định được idempotent không đọc file lần hai.
    // 3A — Arrange: reset + CountingFileReader. Act: EnsureWarmedUpAsync hai lần. Assert: số lần gọi ReadLines/ReadLinesAsync không tăng sau lần một.
    [Fact]
    public async Task WU01_EnsureWarmedUpAsync_SecondCall_ShouldNotInvokeReaderAgain()
    {
        ResetWarmUpFlag();
        var reader = new CountingFileReader();
        await WarmUp.EnsureWarmedUpAsync(reader);

        var syncAfterFirst = reader.ReadLinesCallCount;
        var asyncAfterFirst = reader.ReadLinesAsyncCallCount;
        Assert.True(syncAfterFirst > 0);
        Assert.True(asyncAfterFirst > 0);

        await WarmUp.EnsureWarmedUpAsync(reader);

        Assert.Equal(syncAfterFirst, reader.ReadLinesCallCount);
        Assert.Equal(asyncAfterFirst, reader.ReadLinesAsyncCallCount);
    }

    // F.I.R.S.T — progress callback có văn bản kỳ vọng.
    // 3A — Arrange: reset + list log. Act: warm với callback. Assert: có chuỗi “Warm-up” và “completed”.
    [Fact]
    public async Task WU02_EnsureWarmedUpAsync_ShouldInvokeProgressCallbacks()
    {
        ResetWarmUpFlag();
        var log = new List<string>();
        await WarmUp.EnsureWarmedUpAsync(new FileReaderService(), log.Add);

        Assert.Contains(log, static s => s.Contains("Warm-up", StringComparison.Ordinal));
        Assert.Contains(log, static s => s.Contains("completed", StringComparison.OrdinalIgnoreCase));
    }
}

// IFileReader bọc đếm: biết WarmUp có gọi sync/async hay không.
internal sealed class CountingFileReader : IFileReader
{
    private readonly FileReaderService _inner = new();

    public int ReadLinesCallCount { get; private set; }

    public int ReadLinesAsyncCallCount { get; private set; }

    public IEnumerable<string> ReadLines(string path)
    {
        ReadLinesCallCount++;
        return _inner.ReadLines(path);
    }

    public IAsyncEnumerable<string> ReadLinesAsync(string path, CancellationToken cancellationToken = default)
    {
        ReadLinesAsyncCallCount++;
        return _inner.ReadLinesAsync(path, cancellationToken);
    }
}
