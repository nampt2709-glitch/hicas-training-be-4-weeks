namespace LogAnalyzer;

// Hai pha đo đọc file (Sync rồi Async), sau đó ba phép đếm tần suất token (Sequential / Parallel.ForEach / PLINQ).
public interface IBenchmarkRunner
{
    Task<BenchmarkReport> RunAsync(
        string path,
        AnalysisMode mode,
        Action<string>? progress = null);
}
