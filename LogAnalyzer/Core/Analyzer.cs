using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LogAnalyzer;

// Tách token từ chuỗi lớn: Word (regex) hoặc Error (mapping catalog).
public static class Analyzer
{
    private static readonly Regex WordRegex = new(
        @"[\p{L}\p{N}']+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static AnalysisMode DetectMode(string fileName)
    {
        return fileName.Contains("ErrorLog", StringComparison.OrdinalIgnoreCase)
            ? AnalysisMode.Error
            : AnalysisMode.Word;
    }

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

            if (ErrorCatalog.TryGetCanonicalForTrimmed(token, out var canonical))
            {
                errorList.Add(canonical);
            }
        }

        return errorList;
    }

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

public static class ErrorCatalog
{
    public static readonly IReadOnlyList<string> AllTerms = BuildAllTerms();

    private static readonly IReadOnlyDictionary<string, string> Lookup =
        AllTerms.ToDictionary(term => term, term => term, StringComparer.OrdinalIgnoreCase);

    public static bool TryGetCanonical(string token, out string canonical)
    {
        return Lookup.TryGetValue(token.Trim(), out canonical!);
    }

    internal static bool TryGetCanonicalForTrimmed(string trimmedToken, out string canonical)
    {
        return Lookup.TryGetValue(trimmedToken, out canonical!);
    }

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
