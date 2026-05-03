using System.Collections.Generic; // List, IEnumerable, IReadOnlyList, Dictionary.
using System.Linq; // ToDictionary.
using System.Text.RegularExpressions; // Regex, Match.

namespace LogAnalyzer; // Không gian tên dự án.

// Lớp tĩnh: tách từ (regex) hoặc thuật ngữ lỗi (catalog) từ một dòng/chuỗi.
public static class Analyzer
{
    private static readonly Regex WordRegex = new( // Biên dịch sẵn regex tách token chữ/số.
        @"[\p{L}\p{N}']+", // Một hoặc nhiều chữ cái Unicode, số, hoặc dấu nháy đơn.
        RegexOptions.Compiled | RegexOptions.CultureInvariant); // Tối ưu JIT; không phụ thuộc culture khi match.

    // Nhiệm vụ: quyết định Word hay Error từ tên file. Cách làm: chứa "ErrorLog" → Error, ngược lại Word.
    public static AnalysisMode DetectMode(string fileName)
    {
        return fileName.Contains("ErrorLog", StringComparison.OrdinalIgnoreCase) // Tên file log lỗi chuẩn.
            ? AnalysisMode.Error // Chế độ chỉ đếm thuật ngữ catalog.
            : AnalysisMode.Word; // Chế độ đếm mọi từ regex.
    }

    // Nhiệm vụ: trả list token vật liệu hóa (warm-up hoặc test). Cách làm: Match toàn chuỗi rồi nhánh Word/Error.
    public static List<string> MaterializeTokens(string text, AnalysisMode mode)
    {
        if (string.IsNullOrWhiteSpace(text)) // Không có nội dung.
        {
            return new List<string>(); // List rỗng.
        }

        var matches = WordRegex.Matches(text); // Tìm mọi token trên một lần quét.
        if (mode == AnalysisMode.Word) // Nhánh đếm từ thường.
        {
            var list = new List<string>(matches.Count); // Dự trữ dung lượng tối đa.
            for (var i = 0; i < matches.Count; i++) // Duyệt chỉ số match.
            {
                var value = matches[i].Value.Trim(); // Lấy chuỗi khớp, bỏ khoảng.
                if (value.Length == 0) // Token rỗng sau trim.
                {
                    continue; // Bỏ qua.
                }

                list.Add(value.ToLowerInvariant()); // Chuẩn hóa chữ thường invariant.
            }

            return list; // Danh sách từ đã chuẩn.
        }

        var errorList = new List<string>(Math.Min(matches.Count, 512)); // Giới hạn dự phòng cho nhánh lỗi.
        for (var i = 0; i < matches.Count; i++) // Duyệt match.
        {
            var token = matches[i].Value.Trim(); // Token đã trim.
            if (token.Length == 0) // Bỏ rỗng.
            {
                continue;
            }

            if (ErrorCatalog.TryGetCanonicalForTrimmed(token, out var canonical)) // Chỉ giữ nếu có trong catalog.
            {
                errorList.Add(canonical); // Thêm dạng chuẩn hóa catalog.
            }
        }

        return errorList; // Danh sách lỗi đã lọc.
    }

    // Nhiệm vụ: lazy yield từng từ lowercase. Cách làm: yield break nếu rỗng; foreach match yield return.
    public static IEnumerable<string> ExtractWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) // Không có text.
        {
            yield break; // Kết thúc iterator không phần tử.
        }

        foreach (Match match in WordRegex.Matches(text)) // Mỗi khớp regex.
        {
            var value = match.Value.Trim(); // Trim token.
            if (value.Length == 0) // Bỏ rỗng.
            {
                continue;
            }

            yield return value.ToLowerInvariant(); // Trả từ chữ thường cho caller.
        }
    }

    // Nhiệm vụ: lazy yield thuật ngữ lỗi có trong catalog. Cách làm: mỗi match thử TryGetCanonical.
    public static IEnumerable<string> ExtractErrorTerms(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) // Rỗng.
        {
            yield break; // Không yield.
        }

        foreach (Match match in WordRegex.Matches(text)) // Token hóa giống Word.
        {
            var token = match.Value.Trim(); // Chuẩn hóa biên token.
            if (token.Length == 0) // Bỏ rỗng.
            {
                continue;
            }

            if (ErrorCatalog.TryGetCanonical(token, out var canonical)) // Lookup catalog (trim trong TryGetCanonical).
            {
                yield return canonical; // Trả tên lỗi chuẩn.
            }
        }
    }
}

// Lớp tĩnh: danh mục 200 thuật ngữ lỗi và tra cứu không phân biệt hoa thường.
public static class ErrorCatalog
{
    public static readonly IReadOnlyList<string> AllTerms = BuildAllTerms(); // Khởi tạo tĩnh một lần khi load type.

    private static readonly IReadOnlyDictionary<string, string> Lookup = // Map token → chính nó (canonical).
        AllTerms.ToDictionary(term => term, term => term, StringComparer.OrdinalIgnoreCase); // Khóa so sánh OrdinalIgnoreCase.

    // Nhiệm vụ: tra cứu token (có trim). Cách làm: TryGetValue trên Lookup.
    public static bool TryGetCanonical(string token, out string canonical)
    {
        return Lookup.TryGetValue(token.Trim(), out canonical!); // out canonical được gán nếu tìm thấy.
    }

    internal static bool TryGetCanonicalForTrimmed(string trimmedToken, out string canonical) // API nội bộ khi đã trim.
    {
        return Lookup.TryGetValue(trimmedToken, out canonical!); // Không trim lần hai.
    }

    // Nhiệm vụ: lấy mẫu ngẫu nhiên count phần tử từ catalog. Cách làm: Fisher–Yates shuffle rồi copy đầu.
    public static string[] GetRandomSample(int count, Random? random = null)
    {
        random ??= Random.Shared; // Mặc định RNG luồng an toàn.

        var pool = AllTerms.ToArray(); // Bản sao mảng để xáo.
        var takeCount = Math.Min(count, pool.Length); // Không vượt quá 200.

        for (var i = pool.Length - 1; i > 0; i--) // Shuffle từ cuối về đầu.
        {
            var j = random.Next(i + 1); // Chỉ số ngẫu nhiên 0..i.
            (pool[i], pool[j]) = (pool[j], pool[i]); // Hoán đổi phần tử.
        }

        var result = new string[takeCount]; // Mảng kết quả đúng kích thước.
        Array.Copy(pool, result, takeCount); // Sao chép takeCount phần tử đầu (đã xáo).
        return result; // Trả mẫu.
    }

    // Nhiệm vụ: xây đúng 200 chuỗi: 50 exception + 10×15 biến thể prefix. Cách làm: local Add + kiểm tra Count.
    private static IReadOnlyList<string> BuildAllTerms()
    {
        var terms = new List<string>(200); // Danh sách kết quả cố định 200.
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Tránh trùng không phân biệt hoa thường.

        void Add(string term) // Hàm local thêm nếu chưa có.
        {
            if (set.Add(term)) // HashSet.Add trả false nếu đã tồn tại.
            {
                terms.Add(term); // Chỉ list khi set chấp nhận phần tử mới.
            }
        }

        string[] baseTerms = // Nhóm tên exception .NET và kiểu tương đương (50 mục).
        {
            "NullReferenceException", // Catalog: exception null reference.
            "InvalidOperationException", // Catalog: trạng thái không hợp lệ.
            "ArgumentException", // Catalog: tham số sai.
            "AccessViolationException", // Catalog: truy cập bộ nhớ trái phép.
            "DivideByZeroException", // Catalog: chia cho không.
            "OutOfMemoryException", // Catalog: hết bộ nhớ.
            "StackOverflowException", // Catalog: tràn stack.
            "IOException", // Catalog: lỗi I/O.
            "SqlException", // Catalog: lỗi SQL.
            "UnauthorizedAccessException", // Catalog: không được phép.
            "TimeoutException", // Catalog: hết thời gian chờ.
            "OperationCanceledException", // Catalog: thao tác bị hủy.
            "ObjectDisposedException", // Catalog: đối tượng đã dispose.
            "FileNotFoundException", // Catalog: không tìm thấy file.
            "DirectoryNotFoundException", // Catalog: không tìm thấy thư mục.
            "KeyNotFoundException", // Catalog: không có khóa.
            "FormatException", // Catalog: định dạng sai.
            "NotSupportedException", // Catalog: không hỗ trợ.
            "IndexOutOfRangeException", // Catalog: chỉ số ngoài phạm vi.
            "OverflowException", // Catalog: tràn số.
            "AuthenticationException", // Catalog: xác thực thất bại.
            "SecurityException", // Catalog: bảo mật.
            "SocketException", // Catalog: socket.
            "HttpRequestException", // Catalog: HTTP request.
            "TaskCanceledException", // Catalog: task bị hủy.
            "AggregateException", // Catalog: gói nhiều exception.
            "Win32Exception", // Catalog: mã Win32.
            "EndOfStreamException", // Catalog: hết stream.
            "PathTooLongException", // Catalog: đường dẫn quá dài.
            "JsonException", // Catalog: JSON.
            "InvalidCastException", // Catalog: ép kiểu sai.
            "ApplicationException", // Catalog: ứng dụng (legacy).
            "MissingMethodException", // Catalog: thiếu method.
            "TypeLoadException", // Catalog: load type thất bại.
            "BadImageFormatException", // Catalog: PE sai định dạng.
            "InsufficientMemoryException", // Catalog: bộ nhớ không đủ.
            "ArithmeticException", // Catalog: số học.
            "AmbiguousMatchException", // Catalog: reflection mơ hồ.
            "ParseException", // Catalog: parse.
            "ThreadAbortException", // Catalog: luồng bị abort (legacy).
            "RpcException", // Catalog: RPC.
            "SerializationException", // Catalog: tuần tự hóa.
            "WebException", // Catalog: web (legacy).
            "InvalidProgramException", // Catalog: IL không hợp lệ.
            "CryptographicException", // Catalog: mã hóa.
            "DirectoryNotEmptyException", // Catalog: thư mục không rỗng.
            "EncoderFallbackException", // Catalog: encoder fallback.
            "DecoderFallbackException", // Catalog: decoder fallback.
            "ReflectionTypeLoadException", // Catalog: load nhiều type lỗi.
            "ProtocolViolationException" // Catalog: vi phạm giao thức.
        };

        foreach (var term in baseTerms) // Thêm từng exception cố định.
        {
            Add(term); // Gọi local Add.
        }

        string[] prefixes = // Tiền tố sinh thêm 150 mục (10×15).
        {
            "SystemError", // Nhóm prefix hệ thống.
            "ApplicationError", // Nhóm prefix ứng dụng.
            "NetworkError", // Nhóm prefix mạng.
            "DatabaseError", // Nhóm prefix CSDL.
            "ValidationError", // Nhóm prefix validation.
            "RuntimeError", // Nhóm prefix runtime.
            "ServiceError", // Nhóm prefix dịch vụ.
            "DependencyError", // Nhóm prefix dependency.
            "SecurityError", // Nhóm prefix bảo mật.
            "StorageError" // Nhóm prefix lưu trữ.
        };

        foreach (var prefix in prefixes) // Mỗi tiền tố.
        {
            for (var i = 1; i <= 15; i++) // Số thứ tự 001..015.
            {
                Add($"{prefix}{i:000}"); // Nối prefix + ba chữ số.
            }
        }

        if (terms.Count != 200) // Ràng buộc thiết kế: đúng 200 thuật ngữ.
        {
            throw new InvalidOperationException( // Lỗi lập trình nếu sai số.
                $"Error catalog must contain exactly 200 terms, but currently has {terms.Count}."); // Thông điệp có count thực tế.
        }

        return terms; // Trả danh sách chỉ đọc logic (List as IReadOnlyList qua gán).
    }
}
