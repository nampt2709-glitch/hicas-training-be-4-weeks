using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LogAnalyzer;

// Chia làm 2 chế độ: 
// Word = đếm tần suất từ (token chữ/số);
// Error = chỉ đếm các token trùng với catalog lỗi cố định.
public enum AnalysisMode
{
    Word = 0,
    Error = 1
}

// <summary>
// Tách token từ một dòng/chuỗi lớn: hoặc là "từ" tổng quát, hoặc là "thuật ngữ lỗi" đã chuẩn hoá theo ErrorCatalog.
// </summary>
public static class Analyzer
{
    // Biểu thức chính quy: dãy liền ký tự chữ (Unicode Letter), số (Number), và dấu nháy đơn — mỗi match là một token.
    private static readonly Regex WordRegex = new(
        @"[\p{L}\p{N}']+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Nếu tên file chứa "ErrorLog" (không phân biệt hoa thường) thì giả định file log lỗi -> chế độ Error; ngược lại Word.
    public static AnalysisMode DetectMode(string fileName)
    {
        return fileName.Contains("ErrorLog", StringComparison.OrdinalIgnoreCase)
            ? AnalysisMode.Error
            : AnalysisMode.Word;
    }

    // Mot lan WordRegex.Matches + vong for: cung quy tac ExtractWords/ExtractErrorTerms; list san cho Parallel/PLINQ.
    public static List<string> MaterializeTokens(string text, AnalysisMode mode)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        var matches = WordRegex.Matches(text);
        if (mode == AnalysisMode.Word)
        {
            var list = new List<string>(matches.Count);
            for (var i = 0; i < matches.Count; i++)
            {
                var value = matches[i].Value.Trim();
                if (value.Length == 0)
                {
                    continue;
                }

                list.Add(value.ToLowerInvariant());
            }

            return list;
        }

        var errorList = new List<string>(Math.Min(matches.Count, 512));
        for (var i = 0; i < matches.Count; i++)
        {
            var token = matches[i].Value.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            if (ErrorCatalog.TryGetCanonical(token, out var canonical))
            {
                errorList.Add(canonical);
            }
        }

        return errorList;
    }

    // Lay tat ca token tu text, chuyen ve chu thuong, bo token rong — dung cho dem tu trong file van ban bat ky.
    public static IEnumerable<string> ExtractWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (Match match in WordRegex.Matches(text))
        {
            var value = match.Value.Trim();
            if (value.Length == 0)
            {
                continue;
            }

            yield return value.ToLowerInvariant();
        }
    }

    // Với mỗi token, nếu nằm trong ErrorCatalog thì xuất ra dạng chuẩn (canonical); token không thuộc catalog bỏ qua.
    // Như vậy đếm Error chỉ tính các lỗi đã định nghĩa, bỏ qua "Module", "Code", số, v.v.
    public static IEnumerable<string> ExtractErrorTerms(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (Match match in WordRegex.Matches(text))
        {
            var token = match.Value.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            if (ErrorCatalog.TryGetCanonical(token, out var canonical))
            {
                yield return canonical;
            }
        }
    }
}

// <summary>
// Catalog cố định 200 thuật ngữ lỗi + từ điển tra cứu không phân biệt hoa thường + hàm lấy mẫu ngẫu nhiên.
// </summary>
public static class ErrorCatalog
{
    public static readonly IReadOnlyList<string> AllTerms = BuildAllTerms();

    // Từ điển: key và value cùng là term (lookup nhanh token -> dạng chuẩn).
    private static readonly IReadOnlyDictionary<string, string> Lookup =
        AllTerms.ToDictionary(term => term, term => term, StringComparer.OrdinalIgnoreCase);

    // Tra cứu token (trim) trong catalog; trả về true và canonical nếu có.
    public static bool TryGetCanonical(string token, out string canonical)
    {
        return Lookup.TryGetValue(token.Trim(), out canonical!);
    }

    // Lấy count phần tử ngẫu nhiên không lặp từ pool 200: xáo Fisher-Yates trên mảng copy, lấy count phần tử đầu.
    public static string[] GetRandomSample(int count, Random? random = null)
    {
        random ??= Random.Shared;

        var pool = AllTerms.ToArray();
        var takeCount = Math.Min(count, pool.Length);

        for (var i = pool.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        var result = new string[takeCount];
        Array.Copy(pool, result, takeCount);
        return result;
    }

    // Xây dựng đúng 200 term: ~50 tên exception + 10 prefix × 15 biến số = 150 -> tổng 200 (kiểm tra assert).
    private static IReadOnlyList<string> BuildAllTerms()
    {
        var terms = new List<string>(200);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string term)
        {
            if (set.Add(term))
            {
                terms.Add(term);
            }
        }

        string[] baseTerms =
        {
            "NullReferenceException",
            "InvalidOperationException",
            "ArgumentException",
            "AccessViolationException",
            "DivideByZeroException",
            "OutOfMemoryException",
            "StackOverflowException",
            "IOException",
            "SqlException",
            "UnauthorizedAccessException",
            "TimeoutException",
            "OperationCanceledException",
            "ObjectDisposedException",
            "FileNotFoundException",
            "DirectoryNotFoundException",
            "KeyNotFoundException",
            "FormatException",
            "NotSupportedException",
            "IndexOutOfRangeException",
            "OverflowException",
            "AuthenticationException",
            "SecurityException",
            "SocketException",
            "HttpRequestException",
            "TaskCanceledException",
            "AggregateException",
            "Win32Exception",
            "EndOfStreamException",
            "PathTooLongException",
            "JsonException",
            "InvalidCastException",
            "ApplicationException",
            "MissingMethodException",
            "TypeLoadException",
            "BadImageFormatException",
            "InsufficientMemoryException",
            "ArithmeticException",
            "AmbiguousMatchException",
            "ParseException",
            "ThreadAbortException",
            "RpcException",
            "SerializationException",
            "WebException",
            "InvalidProgramException",
            "CryptographicException",
            "DirectoryNotEmptyException",
            "EncoderFallbackException",
            "DecoderFallbackException",
            "ReflectionTypeLoadException",
            "ProtocolViolationException"
        };

        foreach (var term in baseTerms)
        {
            Add(term);
        }

        string[] prefixes =
        {
            "SystemError",
            "ApplicationError",
            "NetworkError",
            "DatabaseError",
            "ValidationError",
            "RuntimeError",
            "ServiceError",
            "DependencyError",
            "SecurityError",
            "StorageError"
        };

        foreach (var prefix in prefixes)
        {
            for (var i = 1; i <= 15; i++)
            {
                Add($"{prefix}{i:000}");
            }
        }

        if (terms.Count != 200)
        {
            throw new InvalidOperationException(
                $"Error catalog must contain exactly 200 terms, but currently has {terms.Count}.");
        }

        return terms;
    }
}