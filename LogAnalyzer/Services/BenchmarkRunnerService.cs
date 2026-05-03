using System.Diagnostics; // Stopwatch đo thời gian.
using System.Runtime.InteropServices; // CollectionsMarshal cho Dictionary.

namespace LogAnalyzer; // Không gian tên dự án.

// Lớp: chạy benchmark đọc Sync/Async rồi đếm Sequential / Parallel.ForEach / PLINQ trên stream (tránh OOM file lớn).
public sealed class BenchmarkRunnerService : IBenchmarkRunner // Triển khai runner.
{
    private readonly IFileReader _fileReader; // Nguồn đọc dòng (inject).

    // Nhiệm vụ: lưu IFileReader để dùng trong mọi pha đọc/đếm. Cách làm: gán field readonly.
    public BenchmarkRunnerService(IFileReader fileReader)
    {
        _fileReader = fileReader; // Giữ tham chiếu reader.
    }

    // Nhiệm vụ: warm-up, đo đọc, đo đếm ba cách, dựng BenchmarkReport. Cách làm: Stopwatch từng khối; Sequential giữ Items đầy đủ.
    public async Task<BenchmarkReport> RunAsync(
        string path, // Đường file log.
        AnalysisMode mode, // Word hoặc Error.
        Action<string>? progress = null) // Log tiến độ tùy chọn.
    {
        await WarmUp.EnsureWarmedUpAsync(_fileReader, progress).ConfigureAwait(false); // JIT một lần, không bắt sync context.
        progress?.Invoke("Chuẩn bị benchmark (đọc file theo luồng, không nạp cả file vào RAM)..."); // Thông báo chuẩn bị.

        var readRuns = new List<BenchmarkRun>(2); // Hai kết quả đọc: Sync và Async.

        progress?.Invoke("Đang chạy: đọc Sync (ReadLines — duyệt toàn file)"); // Báo bắt đầu đọc đồng bộ.
        var swReadSync = Stopwatch.StartNew(); // Bắt đầu đồng hồ Sync.
        long syncLineCount = 0; // Bộ đếm dòng Sync.
        foreach (var _ in _fileReader.ReadLines(path)) // Duyệt lazy từng dòng không lưu list.
        {
            syncLineCount++; // Tăng đếm mỗi dòng.
        }

        swReadSync.Stop(); // Dừng đồng hồ.
        progress?.Invoke($"Hoàn thành: đọc Sync ({swReadSync.ElapsedMilliseconds} ms, {syncLineCount:n0} dòng)"); // Báo ms và số dòng.
        readRuns.Add(new BenchmarkRun( // Ghi nhận kết quả đọc Sync.
            "Đọc đồng bộ (Sync ReadLines)", // Nhãn hiển thị.
            swReadSync.ElapsedMilliseconds, // Thời gian ms.
            new List<FrequencyItem>())); // Không có tần suất ở pha đọc.

        progress?.Invoke("Đang chạy: đọc Async (ReadLinesAsync — duyệt toàn file)"); // Báo đọc async.
        var swReadAsync = Stopwatch.StartNew(); // Đồng hồ Async.
        long asyncLineCount = 0; // Đếm dòng Async.
        await foreach (var _ in _fileReader.ReadLinesAsync(path).ConfigureAwait(false)) // IAsyncEnumerable từng dòng.
        {
            asyncLineCount++; // Tăng đếm.
        }

        swReadAsync.Stop(); // Dừng đồng hồ.
        progress?.Invoke($"Hoàn thành: đọc Async ({swReadAsync.ElapsedMilliseconds} ms, {asyncLineCount:n0} dòng)"); // Báo kết quả.
        readRuns.Add(new BenchmarkRun( // Ghi nhận Async.
            "Đọc bất đồng bộ (Async ReadLinesAsync)", // Nhãn.
            swReadAsync.ElapsedMilliseconds, // ms.
            new List<FrequencyItem>())); // Không Items.

        if (asyncLineCount != syncLineCount) // Hai lần đọc phải cùng số dòng.
        {
            progress?.Invoke( // Cảnh báo không khớp (hiếm).
                $"Cảnh báo: số dòng Sync ({syncLineCount:n0}) khác Async ({asyncLineCount:n0})."); // Chuỗi cảnh báo.
        }

        var countRuns = new List<BenchmarkRun>(3); // Ba phép đếm.

        countRuns.Add(await MeasureCountAsync( // Đo Sequential.
            "Đếm tuần tự (Sequential)", // Nhãn.
            () => Task.FromResult(CountSequentialStream(path, mode)), // Gọi đếm tuần tự đồng bộ bọc Task.
            attachItems: true, // Lưu list FrequencyItem đầy đủ cho báo cáo.
            progress).ConfigureAwait(false)); // Không marshal về sync context.

        countRuns.Add(await MeasureCountAsync( // Đo Parallel.ForEach.
            "Đếm Parallel.ForEach", // Nhãn.
            () => Task.FromResult(CountParallelForEachStream(path, mode, buildItems: true)), // Gồm sort để công bằng.
            attachItems: false, // Không lưu duplicate list trong RAM.
            progress).ConfigureAwait(false));

        countRuns.Add(await MeasureCountAsync( // Đo PLINQ.
            "Đếm PLINQ", // Nhãn.
            () => Task.FromResult(CountPlinqStream(path, mode, buildItems: true)), // Gồm sort.
            attachItems: false, // Không lưu list trong run.
            progress).ConfigureAwait(false));

        var frequencyItems = countRuns[0].Items; // Chỉ Sequential có Items đầy đủ.
        var totalOccurrences = SumTotalCount(frequencyItems); // Cộng tất cả Count.
        var distinctTypeCount = frequencyItems.Count; // Số hàng trong bảng tần suất.

        progress?.Invoke("Benchmark hoàn tất."); // Kết thúc toàn pipeline.

        return new BenchmarkReport( // Đóng gói báo cáo bất biến.
            mode, // Chế độ.
            path, // File nguồn.
            readRuns, // Hai pha đọc.
            countRuns, // Ba pha đếm.
            frequencyItems, // Danh sách xuất file/console.
            totalOccurrences, // Tổng lần xuất hiện.
            distinctTypeCount); // Số loại.
    }

    // Nhiệm vụ: bọc một lần đếm bằng Stopwatch và tùy chọn lưu Items. Cách làm: StartNew → await action → Stop → new BenchmarkRun.
    private static async Task<BenchmarkRun> MeasureCountAsync(
        string label, // Tên hiển thị.
        Func<Task<List<FrequencyItem>>> action, // Hàm async trả list kết quả (có thể rỗng).
        bool attachItems, // Có gắn list vào run hay chỉ đo (bỏ để tiết kiệm RAM).
        Action<string>? progress) // Callback log.
    {
        progress?.Invoke($"Đang chạy: {label}"); // Báo bắt đầu phép đo.

        var sw = Stopwatch.StartNew(); // Đồng hồ.

        var items = await action().ConfigureAwait(false); // Chạy đếm (Sequential/Parallel/PLINQ).

        sw.Stop(); // Dừng.

        progress?.Invoke($"Hoàn thành: {label} ({sw.ElapsedMilliseconds} ms)"); // Báo thời gian.

        var storedItems = attachItems ? items : new List<FrequencyItem>(); // Giữ hoặc vứt list kết quả.
        return new BenchmarkRun(label, sw.ElapsedMilliseconds, storedItems); // Gói kết quả một lần chạy.
    }

    // Nhiệm vụ: một lượt ReadLines + cập nhật một Dictionary + SortDictionary. Cách làm: foreach line AddLineToCounts.
    private List<FrequencyItem> CountSequentialStream(string path, AnalysisMode mode)
    {
        var dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // Từ điển đếm không phân biệt hoa thường.

        foreach (var line in _fileReader.ReadLines(path)) // Mỗi dòng một lần.
        {
            AddLineToCounts(dictionary, line, mode); // Trích token và tăng bộ đếm.
        }

        return SortDictionary(dictionary); // Sắp xếp giảm count → list báo cáo.
    }

    // Nhiệm vụ: Parallel.ForEach trên dòng, merge dict, tùy chọn sort. Cách làm: local init, body AddLineToCounts, finally lock gom.
    private List<FrequencyItem> CountParallelForEachStream(string path, AnalysisMode mode, bool buildItems)
    {
        var dictionaries = new List<Dictionary<string, int>>(); // Chứa dict từng worker.
        var lockObj = new object(); // Khóa gom list.

        Parallel.ForEach( // API song song.
            _fileReader.ReadLines(path), // Nguồn dòng (partition nội bộ).
            () => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), // Khởi tạo thread-local dict.
            (line, _, local) => // Xử lý một dòng với state local.
            {
                AddLineToCounts(local, line, mode); // Đếm vào dict cục bộ.
                return local; // Trả state cho lần sau.
            },
            local => // Khi partition của worker kết thúc.
            {
                if (local.Count == 0) // Dict rỗng.
                {
                    return; // Không gom.
                }

                lock (lockObj) // Đồng bộ thêm vào danh sách.
                {
                    dictionaries.Add(local); // Lưu dict cục bộ.
                }
            });

        var merged = MergeDictionariesOnly(dictionaries); // Gộp tuần tự tất cả dict cục bộ.
        return buildItems ? SortDictionary(merged) : new List<FrequencyItem>(); // Sort nếu benchmark công bằng.
    }

    // Nhiệm vụ: PLINQ Aggregate trên dòng. Cách làm: AsParallel + Aggregate seed/update/MergeCounts.
    private List<FrequencyItem> CountPlinqStream(string path, AnalysisMode mode, bool buildItems)
    {
        var merged = _fileReader.ReadLines(path) // Bắt đầu từ dòng sync enumerable.
            .AsParallel() // Bật PLINQ.
            .WithExecutionMode(ParallelExecutionMode.ForceParallelism) // Không chạy đơn luồng lén lút.
            .WithDegreeOfParallelism(Environment.ProcessorCount) // Giới hạn mức song song ~ số lõi.
            .Aggregate( // Gộp map-reduce.
                () => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), // Seed factory.
                (local, line) => // Cập nhật một dòng.
                {
                    AddLineToCounts(local, line, mode); // Đếm vào partial dict.
                    return local; // Trả cùng tham chiếu.
                },
                MergeCounts, // Combiner hai partial dict.
                local => local); // Kết quả cuối aggregate.

        return buildItems ? SortDictionary(merged) : new List<FrequencyItem>(); // Sort hoặc bỏ qua.
    }

    // Nhiệm vụ: chọn luồng token Word hoặc Error cho một dòng. Cách làm: toán tử ?: giữa hai iterator Analyzer.
    private static IEnumerable<string> AnalyzeText(string line, AnalysisMode mode)
    {
        return mode == AnalysisMode.Error // Nếu chế độ lỗi.
            ? Analyzer.ExtractErrorTerms(line) // Chỉ token trong catalog.
            : Analyzer.ExtractWords(line); // Mọi từ regex.
    }

    // Nhiệm vụ: cộng mọi token của một dòng vào dictionary. Cách làm: foreach token GetValueRefOrAddDefault.
    private static void AddLineToCounts(Dictionary<string, int> dictionary, string line, AnalysisMode mode)
    {
        foreach (var token in AnalyzeText(line, mode)) // Lazy từng token.
        {
            ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, token, out var exists); // Trỏ ô hoặc tạo.
            slot = exists ? slot + 1 : 1; // Tăng hoặc khởi tạo 1.
        }
    }

    // Nhiệm vụ: gộp list nhiều dict thành một. Cách làm: lần lượt MergeCounts.
    private static Dictionary<string, int> MergeDictionariesOnly(List<Dictionary<string, int>> dictionaries)
    {
        if (dictionaries.Count == 0) // Không có dict nào.
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // Trả dict rỗng.
        }

        Dictionary<string, int>? merged = null; // Biến tích lũy.
        foreach (var dictionary in dictionaries) // Từng dict cục bộ.
        {
            merged = merged is null ? dictionary : MergeCounts(merged, dictionary); // Merge lần lượt.
        }

        return merged!; // merged chắc chắn gán ở vòng lặp (Count > 0).
    }

    // Nhiệm vụ: cộng dồn secondary vào primary. Cách làm: hoán vị nếu primary nhỏ hơn; foreach cộng từng khóa.
    private static Dictionary<string, int> MergeCounts(Dictionary<string, int> primary, Dictionary<string, int> secondary)
    {
        if (secondary.Count == 0) // Không có gì gộp.
        {
            return primary; // Giữ primary.
        }

        if (primary.Count < secondary.Count) // Ưu tiên dict lớn hơn làm đích.
        {
            (primary, secondary) = (secondary, primary); // Hoán đổi tham chiếu.
        }

        foreach (var (key, value) in secondary) // Duyệt từng entry phụ.
        {
            ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(primary, key, out var exists); // Ô ở primary.
            slot = exists ? slot + value : value; // Cộng hoặc gán.
        }

        return primary; // Trả dict đã gộp.
    }

    // Nhiệm vụ: materialize FrequencyItem và sort. Cách làm: list + Sort comparer tĩnh.
    private static List<FrequencyItem> SortDictionary(Dictionary<string, int> source)
    {
        if (source.Count == 0) // Rỗng.
        {
            return new List<FrequencyItem>(); // Trả list rỗng.
        }

        var result = new List<FrequencyItem>(source.Count); // Dung lượng khớp số khóa.
        foreach (var (key, value) in source) // Duyệt entry.
        {
            result.Add(new FrequencyItem(key, value)); // Đóng gói record.
        }

        result.Sort(static (a, b) => // Sort tại chỗ.
        {
            var byCount = b.Count.CompareTo(a.Count); // Giảm dần theo Count.
            return byCount != 0
                ? byCount // Ưu tiên count.
                : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name); // Hòa thì theo tên.
        });

        return result; // Danh sách đã sắp.
    }

    // Nhiệm vụ: tính tổng số lần xuất hiện từ list FrequencyItem. Cách làm: long để tránh overflow.
    private static long SumTotalCount(IEnumerable<FrequencyItem> items)
    {
        long total = 0; // Tích lũy.
        foreach (var item in items) // Mỗi mục.
        {
            total += item.Count; // Cộng Count (int) vào long.
        }

        return total; // Tổng occurrences.
    }
}
