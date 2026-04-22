// Parse số trang/ cỡ trang invariant, không phụ thuộc văn hóa máy.
using System.Globalization;

namespace CommentAPI.DTOs;

// Chuẩn hóa page/pageSize (tối thiểu 1, tối đa MaxPageSize); ParseFromQuery đọc chuỗi query, lỗi thì dùng mặc định 1 và 20 rồi Normalize.
public static class PaginationQuery{

    // Khi client không gửi pageSize, dùng 20 dòng mỗi trang mặc định.
    public const int DefaultPageSize = 20;

    // Trần bảo vệ: không cho pageSize lớn quá, tránh quét toàn bảng vô tình.
    public const int MaxPageSize = 100;

    // Cắt biên: trang ≥1; pageSize<1 thì dùng DefaultPageSize, >Max thì cắt về Max — trả tuple cho Skip/Take.
    public static (int Page, int PageSize) Normalize(int page, int pageSize)    {
        var p = page < 1 ? 1 : page; // Trang tối thiểu 1, tránh offset âm ở repository.
        var s = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize); // Nếu pageSize 0/âm, lấy mặc định; nếu quá lớn, cắt ở Max.
        return (p, s); // Tuple truyền tới service/repository tính Skip/Take.
    }

    // Đọc chuỗi query: trống/không phải số → thay 1 / DefaultPageSize, sau đó Normalize.
    public static (int Page, int PageSize) ParseFromQuery(string? pageRaw, string? pageSizeRaw)    {
        var page = ParseIntLoose(pageRaw, 1); // Parse số trang, fallback 1, không ném ngoại lệ lên ngoài.
        var pageSize = ParseIntLoose(pageSizeRaw, DefaultPageSize); // Parse cỡ trang, rỗng thì 20, rồi sắp Normalize bên dưới.
        return Normalize(page, pageSize); // Cắt biên sau khi đã có số; coi mọi chuỗi lỗi thành fallback.
    }

    private static int ParseIntLoose(string? raw, int fallback) // Hàm phụ, không công khai, parse linh hoạt, không throw.
    {
        if (string.IsNullOrWhiteSpace(raw)) // Bỏ qua trắng hoặc thiếu, trả mặc định ủy nhiệm từ caller.
        {
            return fallback; // Dùng giá trị mặc định 1 cho page hoặc DefaultPageSize cho size.
        }

        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) // Parse invariant để 123 không bị môi trường lôi theo dấu phẩy.
            ? v // Nếu thành công, trả số thực tế, có thể vẫn 0/âm → Normalize sẽ xử.
            : fallback; // Không phải số nguyên, im lặng dùng fallback, không 400 vì sản phẩm phân trang mềm.
    }
}
