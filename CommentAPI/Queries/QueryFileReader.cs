namespace CommentAPI.Queries;

/// <summary>
/// Đọc nội dung file SQL trong thư mục Queries (được copy ra output khi build) để tái sử dụng CTE/raw query.
/// </summary>
public static class QueryFileReader
{
    private static readonly string QueriesDirectory = Path.Combine(AppContext.BaseDirectory, "Queries");

    /// <summary>
    /// Đọc toàn bộ text của một file .sql trong thư mục Queries (ví dụ <c>CommentTree_ByPost.sql</c>).
    /// </summary>
    public static string ReadSql(string fileName)
    {
        // Ghép đường dẫn tuyệt đối tới file SQL cạnh assembly để tránh lệ thuộc thư mục làm việc hiện tại.
        var path = Path.Combine(QueriesDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Không tìm thấy file query: {path}", path);
        }

        return File.ReadAllText(path);
    }
}
