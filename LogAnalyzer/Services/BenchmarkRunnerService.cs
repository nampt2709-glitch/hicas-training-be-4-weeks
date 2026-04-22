using System.Diagnostics;

namespace LogAnalyzer;

// Chạy 6 lần đo; phụ thuộc IFileReader (inject qua constructor).
public sealed class BenchmarkRunnerService : IBenchmarkRunner
{
    private readonly IFileReader _fileReader;

    public BenchmarkRunnerService(IFileReader fileReader)
    {
        _fileReader = fileReader;
    }

    public async Task<BenchmarkReport> RunAsync(
        string path,
        AnalysisMode mode,
        Action<string>? progress = null)
    {
        progress?.Invoke("Preparing benchmark...");

        var runs = new List<BenchmarkRun>(6);

        runs.Add(await MeasureAsync("Sync + Sequential", () => Task.FromResult(ExecuteSyncSequential(path, mode)), progress));
        runs.Add(await MeasureAsync("Sync + Parallel.ForEach", () => Task.FromResult(ExecuteSyncParallel(path, mode)), progress));
        runs.Add(await MeasureAsync("Sync + PLINQ", () => Task.FromResult(ExecuteSyncPlinq(path, mode)), progress));

        runs.Add(await MeasureAsync("Async + Sequential", () => ExecuteAsyncSequential(path, mode), progress));
        runs.Add(await MeasureAsync("Async + Parallel.ForEach", () => ExecuteAsyncParallel(path, mode), progress));
        runs.Add(await MeasureAsync("Async + PLINQ", () => ExecuteAsyncPlinq(path, mode), progress));

        List<FrequencyItem>? topItems = null;
        for (var i = 0; i < runs.Count; i++)
        {
            if (runs[i].Items.Count > 0)
            {
                topItems = runs[i].Items;
                break;
            }
        }

        topItems ??= new List<FrequencyItem>();

        progress?.Invoke("Benchmark finished.");

        return new BenchmarkReport(mode, path, runs, topItems);
    }

    private static async Task<BenchmarkRun> MeasureAsync(
        string label,
        Func<Task<List<FrequencyItem>>> action,
        Action<string>? progress)
    {
        progress?.Invoke($"Running: {label}");

        var sw = Stopwatch.StartNew();

        var items = await action().ConfigureAwait(false);

        sw.Stop();

        progress?.Invoke($"Completed: {label} ({sw.ElapsedMilliseconds} ms)");

        return new BenchmarkRun(label, sw.ElapsedMilliseconds, items);
    }

    private List<FrequencyItem> ExecuteSyncSequential(string path, AnalysisMode mode)
    {
        var text = _fileReader.ReadSync(path);
        var items = AnalyzeText(text, mode);
        return Counter.Sequential(items);
    }

    private List<FrequencyItem> ExecuteSyncParallel(string path, AnalysisMode mode)
    {
        var text = _fileReader.ReadSync(path);
        var tokens = Analyzer.MaterializeTokens(text, mode);
        return Counter.ParallelForEach(tokens);
    }

    private List<FrequencyItem> ExecuteSyncPlinq(string path, AnalysisMode mode)
    {
        var text = _fileReader.ReadSync(path);
        var tokens = Analyzer.MaterializeTokens(text, mode);
        return Counter.Plinq(tokens);
    }

    private async Task<List<FrequencyItem>> ExecuteAsyncSequential(string path, AnalysisMode mode)
    {
        var text = await _fileReader.ReadAsync(path).ConfigureAwait(false);
        var items = AnalyzeText(text, mode);
        return Counter.Sequential(items);
    }

    private async Task<List<FrequencyItem>> ExecuteAsyncParallel(string path, AnalysisMode mode)
    {
        var text = await _fileReader.ReadAsync(path).ConfigureAwait(false);
        var tokens = Analyzer.MaterializeTokens(text, mode);
        return Counter.ParallelForEach(tokens);
    }

    private async Task<List<FrequencyItem>> ExecuteAsyncPlinq(string path, AnalysisMode mode)
    {
        var text = await _fileReader.ReadAsync(path).ConfigureAwait(false);
        var tokens = Analyzer.MaterializeTokens(text, mode);
        return Counter.Plinq(tokens);
    }

    private static IEnumerable<string> AnalyzeText(string text, AnalysisMode mode)
    {
        return mode == AnalysisMode.Error
            ? Analyzer.ExtractErrorTerms(text)
            : Analyzer.ExtractWords(text);
    }
}
