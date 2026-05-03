namespace LogAnalyzer;

// Ghi báo cáo (PERFORMANCE + top 50) ra file Results.
public interface IResultWriter
{
    string Write(BenchmarkReport report);
}
