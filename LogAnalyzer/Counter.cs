using System.Collections.Concurrent; // Import namespace để dùng ConcurrentBag (collection an toàn đa luồng)
using System.Diagnostics; // Import để dùng Stopwatch đo thời gian
using System.Linq; // Import LINQ (Select, OrderBy, FirstOrDefault,...)
using System.Runtime.InteropServices; // Import để dùng CollectionsMarshal (tối ưu Dictionary)

namespace LogAnalyzer; // Khai báo namespace của project

public sealed record FrequencyItem(string Name, int Count); // Record chứa 1 token và số lần xuất hiện

public sealed record BenchmarkRun(string Label, long ElapsedMilliseconds, List<FrequencyItem> Items); // Record chứa kết quả 1 lần benchmark

public sealed record BenchmarkReport( // Record chứa toàn bộ báo cáo benchmark
    AnalysisMode Mode, // Chế độ phân tích (Word hoặc Error)
    string SourceFile, // Đường dẫn file nguồn
    IReadOnlyList<BenchmarkRun> Runs, // Danh sách 6 lần chạy benchmark
    IReadOnlyList<FrequencyItem> TopItems); // Top kết quả để hiển thị

public static class BenchmarkRunner // Class chịu trách nhiệm chạy benchmark
{
    public static async Task<BenchmarkReport> RunAsync( // Hàm chính chạy benchmark
        string path, // Đường dẫn file
        AnalysisMode mode, // Chế độ phân tích
        Action<string>? progress = null) // Callback để in tiến trình
    {
        progress?.Invoke("Preparing benchmark..."); // Nếu có callback thì gọi

        var runs = new List<BenchmarkRun>(6); // Tạo list chứa tối đa 6 kết quả

        runs.Add(await MeasureAsync("Sync + Sequential", () => Task.FromResult(ExecuteSyncSequential(path, mode)), progress)); // Chạy sync + sequential
        runs.Add(await MeasureAsync("Sync + Parallel.ForEach", () => Task.FromResult(ExecuteSyncParallel(path, mode)), progress)); // Chạy sync + parallel
        runs.Add(await MeasureAsync("Sync + PLINQ", () => Task.FromResult(ExecuteSyncPlinq(path, mode)), progress)); // Chạy sync + plinq

        runs.Add(await MeasureAsync("Async + Sequential", () => ExecuteAsyncSequential(path, mode), progress)); // Chạy async + sequential
        runs.Add(await MeasureAsync("Async + Parallel.ForEach", () => ExecuteAsyncParallel(path, mode), progress)); // Chạy async + parallel
        runs.Add(await MeasureAsync("Async + PLINQ", () => ExecuteAsyncPlinq(path, mode), progress)); // Chạy async + plinq

        var topItems = runs.FirstOrDefault(r => r.Items.Count > 0)?.Items ?? new List<FrequencyItem>(); // Lấy kết quả đầu tiên có dữ liệu

        progress?.Invoke("Benchmark finished."); // Thông báo hoàn thành

        return new BenchmarkReport(mode, path, runs, topItems); // Trả về báo cáo
    }

    private static async Task<BenchmarkRun> MeasureAsync( // Hàm đo thời gian 1 action
        string label, // Tên test case
        Func<Task<List<FrequencyItem>>> action, // Hàm cần đo
        Action<string>? progress) // Callback log
    {
        progress?.Invoke($"Running: {label}"); // Log bắt đầu

        var sw = Stopwatch.StartNew(); // Khởi động đồng hồ

        var items = await action().ConfigureAwait(false); // Chạy action và chờ kết quả

        sw.Stop(); // Dừng đồng hồ

        progress?.Invoke($"Completed: {label} ({sw.ElapsedMilliseconds} ms)"); // Log kết quả

        return new BenchmarkRun(label, sw.ElapsedMilliseconds, items); // Trả kết quả benchmark
    }

    private static List<FrequencyItem> ExecuteSyncSequential(string path, AnalysisMode mode) // Hàm chạy sync + sequential
    {
        var text = FileReader.ReadSync(path); // Đọc file sync

        var items = AnalyzeText(text, mode); // Tách token

        return Counter.Sequential(items); // Đếm tuần tự
    }

    private static List<FrequencyItem> ExecuteSyncParallel(string path, AnalysisMode mode) // Hàm chạy sync + parallel
    {
        var text = FileReader.ReadSync(path); // Đọc file

        var tokens = Analyzer.MaterializeTokens(text, mode); // Convert sang List

        return Counter.ParallelForEach(tokens); // Đếm song song
    }

    private static List<FrequencyItem> ExecuteSyncPlinq(string path, AnalysisMode mode) // Hàm chạy sync + plinq
    {
        var text = FileReader.ReadSync(path); // Đọc file

        var tokens = Analyzer.MaterializeTokens(text, mode); // Convert sang List

        return Counter.Plinq(tokens); // Đếm bằng PLINQ
    }

    private static async Task<List<FrequencyItem>> ExecuteAsyncSequential(string path, AnalysisMode mode) // Async + sequential
    {
        var text = await FileReader.ReadAsync(path).ConfigureAwait(false); // Đọc file async

        var items = AnalyzeText(text, mode); // Tách token

        return Counter.Sequential(items); // Đếm tuần tự
    }

    private static async Task<List<FrequencyItem>> ExecuteAsyncParallel(string path, AnalysisMode mode) // Async + parallel
    {
        var text = await FileReader.ReadAsync(path).ConfigureAwait(false); // Đọc file

        var tokens = Analyzer.MaterializeTokens(text, mode); // Convert sang List

        return Counter.ParallelForEach(tokens); // Đếm song song
    }

    private static async Task<List<FrequencyItem>> ExecuteAsyncPlinq(string path, AnalysisMode mode) // Async + plinq
    {
        var text = await FileReader.ReadAsync(path).ConfigureAwait(false); // Đọc file

        var tokens = Analyzer.MaterializeTokens(text, mode); // Convert sang List

        return Counter.Plinq(tokens); // Đếm plinq
    }

    private static IEnumerable<string> AnalyzeText(string text, AnalysisMode mode) // Hàm chọn cách tách token
    {
        return mode == AnalysisMode.Error // Nếu là chế độ Error
            ? Analyzer.ExtractErrorTerms(text) // Chỉ lấy error
            : Analyzer.ExtractWords(text); // Ngược lại lấy tất cả word
    }
}

public static class Counter // Class chứa thuật toán đếm
{
    private const int MinTokenCountForParallel = 65_536; // Ngưỡng để dùng parallel

    public static List<FrequencyItem> Sequential(IEnumerable<string> items) // Hàm đếm tuần tự
    {
        var dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // Dictionary lưu word -> count

        foreach (var raw in items) // Duyệt từng token
        {
            var item = Normalize(raw); // Trim token

            if (item.Length == 0) continue; // Bỏ token rỗng

            ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, item, out var exists); // Lấy reference slot

            slot = exists ? slot + 1 : 1; // Nếu tồn tại thì +1, không thì =1
        }

        return Sort(dictionary); // Sắp xếp kết quả
    }

    public static List<FrequencyItem> ParallelForEach(IEnumerable<string> items) // Hàm đếm song song
    {
        var source = items as IList<string> ?? items.ToList(); // Convert sang IList nếu chưa phải

        if (source.Count == 0) return new List<FrequencyItem>(); // Nếu rỗng thì trả về rỗng

        if (source.Count < MinTokenCountForParallel) return Sequential(source); // Nếu ít dữ liệu thì dùng sequential

        var locals = new ConcurrentBag<Dictionary<string, int>>(); // Chứa dictionary của từng thread

        var partitioner = Partitioner.Create(source, loadBalance: true); // Chia dữ liệu

        Parallel.ForEach(
            partitioner, // Dữ liệu đã chia
            () => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), // Mỗi thread có dict riêng
            (raw, _, localDict) => // Hàm xử lý mỗi phần tử
            {
                var item = Normalize(raw); // Trim

                if (item.Length != 0) // Nếu không rỗng
                {
                    ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(localDict, item, out var exists); // Lấy slot
                    count = exists ? count + 1 : 1; // Tăng count
                }

                return localDict; // Trả dict lại
            },
            localDict => // Khi thread hoàn thành
            {
                if (localDict.Count > 0) locals.Add(localDict); // Thêm vào bag
            });

        if (locals.IsEmpty) return new List<FrequencyItem>(); // Nếu không có gì

        Dictionary<string, int>? merged = null; // Dictionary tổng

        foreach (var d in locals) // Gộp từng dict
        {
            merged = merged is null ? d : MergeDictionaries(merged, d); // Gộp lại
        }

        return Sort(merged!); // Sort kết quả
    }

    public static List<FrequencyItem> Plinq(IEnumerable<string> items) // Hàm đếm bằng PLINQ
    {
        var list = items as IList<string> ?? items.ToList(); // Convert sang list

        if (list.Count == 0) return new List<FrequencyItem>(); // Nếu rỗng

        if (list.Count < MinTokenCountForParallel) return Sequential(list); // Nếu ít thì không parallel

        var merged = list
            .AsParallel() // Chạy song song
            .WithExecutionMode(ParallelExecutionMode.ForceParallelism) // Ép chạy parallel
            .WithDegreeOfParallelism(Environment.ProcessorCount) // Số thread = CPU
            .Select(Normalize) // Trim
            .Where(s => s.Length > 0) // Bỏ rỗng
            .Aggregate(
                () => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), // Tạo dict cho mỗi thread
                (dict, item) => // Xử lý mỗi item
                {
                    ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, item, out var exists); // Lấy slot
                    count = exists ? count + 1 : 1; // Tăng count
                    return dict; // Trả dict
                },
                MergeDictionaries, // Gộp dict
                dict => dict); // Trả kết quả cuối

        return Sort(merged); // Sort
    }

    private static string Normalize(string value) // Hàm chuẩn hóa token
    {
        return value.Trim(); // Bỏ khoảng trắng đầu/cuối
    }

    private static Dictionary<string, int> MergeDictionaries(Dictionary<string, int> primary, Dictionary<string, int> secondary) // Gộp 2 dict
    {
        if (secondary.Count == 0) return primary; // Nếu secondary rỗng

        if (primary.Count < secondary.Count) (primary, secondary) = (secondary, primary); // Swap nếu cần

        foreach (var kv in secondary) // Duyệt từng phần tử
        {
            if (primary.TryGetValue(kv.Key, out var c)) // Nếu đã có
                primary[kv.Key] = c + kv.Value; // Cộng dồn
            else
                primary[kv.Key] = kv.Value; // Thêm mới
        }

        return primary; // Trả dict đã gộp
    }

    private static List<FrequencyItem> Sort(IEnumerable<KeyValuePair<string, int>> source) // Hàm sort
    {
        return source
            .Select(x => new FrequencyItem(x.Key, x.Value)) // Convert sang object
            .OrderByDescending(x => x.Count) // Sort giảm dần theo count
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase) // Nếu bằng thì sort theo tên
            .ToList(); // Convert sang List
    }
}