using System.Collections.Concurrent; // Partitioner cho Parallel.ForEach.
using System.Linq; // PLINQ (AsParallel, Aggregate).
using System.Runtime.InteropServices; // CollectionsMarshal tối ưu Dictionary.

namespace LogAnalyzer; // Không gian tên dự án.

// Lớp tĩnh: đếm tần suất chuỗi bằng tuần tự, Parallel.ForEach hoặc PLINQ (dùng trong warm-up hoặc đếm trên list token).
public static class Counter
{
    private const int MinTokenCountForParallel = 65_536; // Ngưỡng tối thiểu token mới bật song song (tránh overhead).

    // Nhiệm vụ: đếm tuần tự và trả danh sách đã sort. Cách làm: một Dictionary, foreach token, Sort cuối.
    public static List<FrequencyItem> Sequential(IEnumerable<string> items)
    {
        var dictionary = items is IList<string> list && list.Count > 0 // Nếu nguồn là list có phần tử.
            ? new Dictionary<string, int>(EstimateDictionaryCapacity(list.Count), StringComparer.OrdinalIgnoreCase) // Khởi tạo capacity ước lượng.
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // Ngược lại từ điển rỗng mặc định.

        foreach (var raw in items) // Duyệt từng token thô.
        {
            var item = Normalize(raw); // Chuẩn hóa (trim).

            if (item.Length == 0) continue; // Bỏ chuỗi rỗng.

            ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, item, out var exists); // Tham chiếu ô đếm hoặc tạo mới.

            slot = exists ? slot + 1 : 1; // Tăng hoặc gán 1.
        }

        return Sort(dictionary); // Chuyển dict thành list FrequencyItem đã sắp xếp.
    }

    // Nhiệm vụ: đếm song song bằng Parallel.ForEach. Cách làm: partition list, local dict, lock gom, merge, sort.
    public static List<FrequencyItem> ParallelForEach(IEnumerable<string> items)
    {
        var source = items as IList<string> ?? items.ToList(); // Vật liệu hóa list nếu cần để đếm và partition.

        if (source.Count == 0) return new List<FrequencyItem>(); // Không có token → kết quả rỗng.

        if (source.Count < MinTokenCountForParallel) return Sequential(source); // Tập nhỏ → tuần tự nhanh hơn.

        var localDicts = new List<Dictionary<string, int>>(); // Chứa các từ điển cục bộ sau worker.
        var localDictsLock = new object(); // Khóa khi thêm vào localDicts.

        var partitioner = Partitioner.Create(source, loadBalance: true); // Chia đều tải giữa worker.

        Parallel.ForEach( // Vòng lặp song song có trạng thái cục bộ.
            partitioner, // Nguồn đã partition.
            () => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), // Factory từ điển thread-local.
            (raw, _, threadLocal) => // Xử lý một phần tử với state threadLocal.
            {
                var item = Normalize(raw); // Chuẩn hóa token.

                if (item.Length != 0) // Bỏ qua rỗng.
                {
                    ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(threadLocal, item, out var exists); // Ô trong dict local.
                    var newVal = exists ? slot + 1 : 1; // Giá trị mới.
                    slot = newVal; // Ghi lại.
                }

                return threadLocal; // Trả state cho bước sau.
            },
            threadLocal => // Khi worker xong một partition.
            {
                if (threadLocal.Count == 0) return; // Dict rỗng thì bỏ qua.
                lock (localDictsLock) // Đồng bộ danh sách.
                {
                    localDicts.Add(threadLocal); // Gom dict cục bộ.
                }
            });

        if (localDicts.Count == 0) return new List<FrequencyItem>(); // Không dict nào → rỗng.

        Dictionary<string, int>? merged = null; // Biến gom merge dần.

        foreach (var d in localDicts) // Merge tuần tự từng dict cục bộ.
        {
            merged = merged is null ? d : MergeDictionaries(merged, d); // Lần đầu gán, sau đó gộp.
        }

        return Sort(merged!); // Sort kết quả cuối (merged chắc chắn không null ở đây).
    }

    // Nhiệm vụ: đếm bằng PLINQ Aggregate. Cách làm: AsParallel, Normalize+Where, Aggregate với MergeDictionaries.
    public static List<FrequencyItem> Plinq(IEnumerable<string> items)
    {
        var list = items as IList<string> ?? items.ToList(); // Cần list để PLINQ ổn định.

        if (list.Count == 0) return new List<FrequencyItem>(); // Rỗng.

        if (list.Count < MinTokenCountForParallel) return Sequential(list); // Ngưỡng nhỏ → tuần tự.

        var merged = list // Bắt đầu pipeline từ list.
            .AsParallel() // Bật thực thi song song.
            .WithExecutionMode(ParallelExecutionMode.ForceParallelism) // Ép dùng worker song song.
            .WithDegreeOfParallelism(Environment.ProcessorCount) // Số luồng ~ số lõi logic.
            .Select(Normalize) // Ánh xạ trim.
            .Where(s => s.Length > 0) // Lọc rỗng.
            .Aggregate( // Gộp song song bằng seed + update + merge + transform.
                () => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), // Factory seed.
                (dict, item) => // Cập nhật một phần tử vào dict local.
                {
                    ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, item, out var exists); // Ô đếm.
                    count = exists ? count + 1 : 1; // Tăng hoặc khởi tạo.
                    return dict; // Trả cùng tham chiếu dict.
                },
                MergeDictionaries, // Hàm gộp hai dict partial.
                dict => dict); // Kết quả cuối aggregate là dict đầy đủ.

        return Sort(merged); // Sắp xếp thành FrequencyItem.
    }

    private static string Normalize(string value) => value.Trim(); // Chuẩn hóa token: bỏ khoảng đầu cuối.

    // Nhiệm vụ: gộp secondary vào primary (cộng dồn count). Cách làm: đổi chỗ nếu primary nhỏ hơn để ít vòng lặp hơn.
    private static Dictionary<string, int> MergeDictionaries(Dictionary<string, int> primary, Dictionary<string, int> secondary)
    {
        if (secondary.Count == 0) return primary; // Không có gì để gộp.

        if (primary.Count < secondary.Count) (primary, secondary) = (secondary, primary); // Ưu tiên dict lớn làm đích.

        foreach (var kv in secondary) // Duyệt từng cặp khóa-giá trị nguồn phụ.
        {
            if (primary.TryGetValue(kv.Key, out var c)) // Khóa đã tồn tại ở primary.
                primary[kv.Key] = c + kv.Value; // Cộng dồn.
            else
                primary[kv.Key] = kv.Value; // Thêm khóa mới.
        }

        return primary; // Trả dict đích đã gộp.
    }

    // Nhiệm vụ: chuyển Dictionary thành List<FrequencyItem> và sort. Cách làm: materialize rồi Sort với comparer.
    private static List<FrequencyItem> Sort(Dictionary<string, int> source)
    {
        if (source.Count == 0) // Không có khóa.
        {
            return new List<FrequencyItem>(); // Trả list rỗng.
        }

        var list = new List<FrequencyItem>(source.Count); // Cấp phát đúng dung lượng.
        foreach (var kv in source) // Duyệt từng entry.
        {
            list.Add(new FrequencyItem(kv.Key, kv.Value)); // Đóng gói record.
        }

        list.Sort(CompareFrequencyItems); // Sắp xếp giảm dần theo Count rồi Name.
        return list; // Trả list đã sort.
    }

    // Nhiệm vụ: so sánh hai FrequencyItem cho Sort. Cách làm: ưu tiên Count lớn hơn; hòa thì so tên không phân biệt hoa thường.
    private static int CompareFrequencyItems(FrequencyItem a, FrequencyItem b)
    {
        var byCount = b.Count.CompareTo(a.Count); // Đảo CompareTo để giảm dần.
        return byCount != 0
            ? byCount // Khác count → dùng count.
            : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name); // Hòa count → so tên.
    }

    // Nhiệm vụ: ước lượng capacity Dictionary từ số token. Cách làm: token/3 clamp trong [16, 1M].
    private static int EstimateDictionaryCapacity(int tokenCount)
    {
        if (tokenCount <= 0) return 16; // Giá trị tối thiểu hợp lý.
        var estimated = tokenCount / 3; // Heuristic số bucket.
        return Math.Clamp(estimated, 16, 1_048_576); // Giới hạn trên để tránh quá lớn.
    }
}
