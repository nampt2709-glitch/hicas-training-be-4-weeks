namespace CommentAPI.Queries;

// Tĩnh: không lưu trạng thái; mỗi lần ReadSql mở file theo tên, thread-safe ở mức gọi File.ReadAllText.
public static class QueryFileReader
{
    // Ghép BaseDirectory (thư mục exe) với tên thư mục Queries — giữ script bên cạnh assembly sau publish.
    private static readonly string QueriesDirectory = Path.Combine(AppContext.BaseDirectory, "Queries"); // Tính một lần, thread-safe hằng số ủy nhiệm từ runtime.

    // Đọc toàn bộ nội dung file .sql trong thư mục Queries (ví dụ CommentTree_ByPost.sql), phải tồn tại; ném FileNotFound nếu thiếu.

    public static string ReadSql(string fileName) // Tên file kèm phần mở rộng, ví dụ "CommentTree_ByPost.sql".
    {
        // Nối thư mục cố định với tên file; không dùng CurrentDirectory vì thay đổi theo cách bật ứng dụng.
        var path = Path.Combine(QueriesDirectory, fileName); // Đường dẫn tuyệt đối dự kiến tới nội dung CTE/Raw SQL.
        if (!File.Exists(path)) // Kiểm tra tồn tại rõ ràng thay vì cho SqlClient báo lỗi khó đọc.
        {
            // Ném FileNotFound với tên file đủ, phục vụ log và sửa thiếu file sau deploy.
            throw new FileNotFoundException($"Không tìm thấy file query: {path}", path); // Ngoại lệ gợi ý kiểm tra copy-to-output ở csproj.
        }

        // Đọc toàn bộ nội dung UTF-8 theo mặc định hệ, phù hợp CTE dài; không dùng stream ở đây (file nhỏ tương đối).
        return File.ReadAllText(path); // Trả chuỗi nội dung SQL, repository gán trực tiếp CommandText.
    }
}
