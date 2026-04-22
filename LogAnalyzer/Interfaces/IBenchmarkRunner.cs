namespace LogAnalyzer;

// Chạy 6 lần so sánh (sync/async × Sequential / Parallel.ForEach / PLINQ).
public interface IBenchmarkRunner
{
    Task<BenchmarkReport> RunAsync(
        string path,
        AnalysisMode mode,
        Action<string>? progress = null);
}
