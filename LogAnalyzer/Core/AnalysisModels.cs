namespace LogAnalyzer; // Không gian tên chứa mô hình dữ liệu phân tích.

// Kiểu liệt kê: chọn cách trích token từ mỗi dòng log.
public enum AnalysisMode
{
    Word = 0, // Đếm mọi từ khớp regex chữ/số.
    Error = 1 // Chỉ đếm thuật ngữ có trong ErrorCatalog.
}

// Bản ghi bất biến: một hàng trong bảng tần suất (tên + số lần).
public sealed record FrequencyItem(string Name, int Count); // Name: khóa; Count: số lần xuất hiện.

// Bản ghi: một lần đo benchmark — wall-clock, CPU tiến trình (cộng dồn luồng), RAM working set sau bước.
public sealed record BenchmarkRun(
    string Label,
    long ElapsedMilliseconds,
    List<FrequencyItem> Items,
    long CpuTimeMilliseconds = 0, // Chênh lệch Process.TotalProcessorTime trong khoảng đo (ms).
    long WorkingSetBytes = 0); // Working Set sau khi bước kết thúc (snapshot; bytes).

// Bản ghi: báo cáo tổng hợp sau khi chạy đủ pha đọc và đếm.
public sealed record BenchmarkReport(
    AnalysisMode Mode, // Word hoặc Error.
    string SourceFile, // Đường dẫn file nguồn.
    IReadOnlyList<BenchmarkRun> ReadRuns, // Hai pha đo đọc Sync/Async.
    IReadOnlyList<BenchmarkRun> CountRuns, // Ba pha đếm Sequential/ForEach/PLINQ.
    IReadOnlyList<FrequencyItem> FrequencyItems, // Danh sách tần suất đầy đủ (từ Sequential).
    long TotalOccurrences, // Tổng số lần xuất hiện (tổng Count).
    int DistinctTypeCount); // Số loại khóa khác nhau (= số phần tử FrequencyItems).
