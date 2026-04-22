namespace LogAnalyzer;

// Chế độ: đếm mọi từ (Word) hoặc chỉ thuật ngữ lỗi trong danh mục (Error).
public enum AnalysisMode
{
    Word = 0,
    Error = 1
}

// Một dòng tần suất: tên mục (từ hoặc mã lỗi) và số lần xuất hiện.
public sealed record FrequencyItem(string Name, int Count);

// Một lần đo benchmark: nhãn, thời gian, danh sách tần suất tại lần đó.
public sealed record BenchmarkRun(string Label, long ElapsedMilliseconds, List<FrequencyItem> Items);

// Báo cáo: chế độ, file, 6 lần chạy, và dữ liệu top để ghi file kết quả.
public sealed record BenchmarkReport(
    AnalysisMode Mode,
    string SourceFile,
    IReadOnlyList<BenchmarkRun> Runs,
    IReadOnlyList<FrequencyItem> TopItems);
