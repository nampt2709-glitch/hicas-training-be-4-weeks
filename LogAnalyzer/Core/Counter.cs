using System.Collections.Concurrent; // Partitioner — chia dữ liệu cho Parallel.ForEach.
using System.Linq; // PLINQ (AsParallel, Aggregate).
using System.Runtime.InteropServices; // CollectionsMarshal.

namespace LogAnalyzer;

// Thuật toán đếm tần suất: tuần tự, Parallel.ForEach, PLINQ.
public static class Counter
{
    private const int MinTokenCountForParallel = 65_536;

    public static List<FrequencyItem> Sequential(IEnumerable<string> items)
    {
        var dictionary = items is IList<string> list && list.Count > 0
            ? new Dictionary<string, int>(EstimateDictionaryCapacity(list.Count), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in items)
        {
            var item = Normalize(raw);

            if (item.Length == 0) continue;

            ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, item, out var exists);

            slot = exists ? slot + 1 : 1;
        }

        return Sort(dictionary);
    }

    public static List<FrequencyItem> ParallelForEach(IEnumerable<string> items)
    {
        var source = items as IList<string> ?? items.ToList();

        if (source.Count == 0) return new List<FrequencyItem>();

        if (source.Count < MinTokenCountForParallel) return Sequential(source);

        var localDicts = new List<Dictionary<string, int>>();
        var localDictsLock = new object();

        var partitioner = Partitioner.Create(source, loadBalance: true);

        Parallel.ForEach(
            partitioner,
            () => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            (raw, _, threadLocal) =>
            {
                var item = Normalize(raw);

                if (item.Length != 0)
                {
                    ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(threadLocal, item, out var exists);
                    var newVal = exists ? slot + 1 : 1;
                    slot = newVal;
                }

                return threadLocal;
            },
            threadLocal =>
            {
                if (threadLocal.Count == 0) return;
                lock (localDictsLock)
                {
                    localDicts.Add(threadLocal);
                }
            });

        if (localDicts.Count == 0) return new List<FrequencyItem>();

        Dictionary<string, int>? merged = null;

        foreach (var d in localDicts)
        {
            merged = merged is null ? d : MergeDictionaries(merged, d);
        }

        return Sort(merged!);
    }

    public static List<FrequencyItem> Plinq(IEnumerable<string> items)
    {
        var list = items as IList<string> ?? items.ToList();

        if (list.Count == 0) return new List<FrequencyItem>();

        if (list.Count < MinTokenCountForParallel) return Sequential(list);

        var merged = list
            .AsParallel()
            .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
            .WithDegreeOfParallelism(Environment.ProcessorCount)
            .Select(Normalize)
            .Where(s => s.Length > 0)
            .Aggregate(
                () => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                (dict, item) =>
                {
                    ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, item, out var exists);
                    count = exists ? count + 1 : 1;
                    return dict;
                },
                MergeDictionaries,
                dict => dict);

        return Sort(merged);
    }

    private static string Normalize(string value) => value.Trim();

    private static Dictionary<string, int> MergeDictionaries(Dictionary<string, int> primary, Dictionary<string, int> secondary)
    {
        if (secondary.Count == 0) return primary;

        if (primary.Count < secondary.Count) (primary, secondary) = (secondary, primary);

        foreach (var kv in secondary)
        {
            if (primary.TryGetValue(kv.Key, out var c))
                primary[kv.Key] = c + kv.Value;
            else
                primary[kv.Key] = kv.Value;
        }

        return primary;
    }

    private static List<FrequencyItem> Sort(Dictionary<string, int> source)
    {
        if (source.Count == 0)
        {
            return new List<FrequencyItem>();
        }

        var list = new List<FrequencyItem>(source.Count);
        foreach (var kv in source)
        {
            list.Add(new FrequencyItem(kv.Key, kv.Value));
        }

        list.Sort(CompareFrequencyItems);
        return list;
    }

    private static int CompareFrequencyItems(FrequencyItem a, FrequencyItem b)
    {
        var byCount = b.Count.CompareTo(a.Count);
        return byCount != 0
            ? byCount
            : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
    }

    private static int EstimateDictionaryCapacity(int tokenCount)
    {
        if (tokenCount <= 0) return 16;
        var estimated = tokenCount / 3;
        return Math.Clamp(estimated, 16, 1_048_576);
    }
}
