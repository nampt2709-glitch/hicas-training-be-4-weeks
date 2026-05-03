using System.Text; // StringBuilder để dựng khối thông báo lỗi.

namespace LogAnalyzer; // Không gian tên dự án.

// Lớp tĩnh: đăng ký bắt exception toàn cục và in ra console an toàn khi đa luồng.
public static class GlobalExceptionHandling
{
    private static readonly object ConsoleLock = new(); // Khóa để tránh xen kẽ chữ khi nhiều luồng ghi Console.

    // Nhiệm vụ: gắn handler cho exception không bắt được. Cách làm: đăng ký sự kiện AppDomain và TaskScheduler.
    public static void Register()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException; // Bắt exception chết trên luồng chính / nền.
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException; // Bắt exception Task bị bỏ quên.
    }

    // Nhiệm vụ: xử lý UnhandledException. Cách làm: in chi tiết nếu là Exception; cảnh báo nếu process sắp thoát.
    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) // Kiểm tra kiểu an toàn.
        {
            WriteExceptionBlock("Unhandled exception (AppDomain.UnhandledException)", ex, includeStackTrace: true); // In khối lỗi có stack.
        }
        else // Trường hợp hiếm: object không phải Exception.
        {
            SafeWriteLine($"Unhandled exception (not an Exception object): {e.ExceptionObject}"); // In thông điệp tối thiểu.
        }

        if (e.IsTerminating) // Runtime sắp kết thúc tiến trình.
        {
            SafeWriteLine("The application is terminating due to an unhandled exception."); // Thông báo cho người dùng.
        }
    }

    // Nhiệm vụ: xử lý exception từ Task không được await. Cách làm: in lỗi rồi đánh dấu đã quan sát để tránh crash mặc định.
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteExceptionBlock("Unobserved task exception (TaskScheduler.UnobservedTaskException)", e.Exception, includeStackTrace: true); // In toàn bộ AggregateException.
        e.SetObserved(); // Đánh dấu đã xử lý, tránh policy mặc định có thể gây crash.
    }

    // Nhiệm vụ: in một khối thông tin exception có tiêu đề. Cách làm: nối chuỗi vào StringBuilder rồi ghi Console thread-safe.
    public static void WriteExceptionBlock(string title, Exception ex, bool includeStackTrace)
    {
        var sb = new StringBuilder(); // Bộ đệm văn bản.
        sb.AppendLine(); // Dòng trống phía trên.
        sb.AppendLine("========== EXCEPTION =========="); // Viền tiêu đề.
        sb.AppendLine(title); // Dòng mô tả ngữ cảnh lỗi.
        sb.AppendLine($"Type: {ex.GetType().FullName}"); // Tên đầy đủ kiểu exception.
        sb.AppendLine($"Message: {ex.Message}"); // Thông điệp lỗi.

        if (includeStackTrace && !string.IsNullOrWhiteSpace(ex.StackTrace)) // Chỉ in stack khi được yêu cầu và có dữ liệu.
        {
            sb.AppendLine("Stack trace:"); // Nhãn stack.
            sb.AppendLine(ex.StackTrace); // Nội dung stack.
        }

        if (ex.InnerException is { } inner) // Pattern matching: có exception lồng.
        {
            sb.AppendLine("--- Inner exception ---"); // Phân tách phần inner.
            sb.AppendLine($"Type: {inner.GetType().FullName}"); // Kiểu inner.
            sb.AppendLine($"Message: {inner.Message}"); // Thông điệp inner.
            if (includeStackTrace && !string.IsNullOrWhiteSpace(inner.StackTrace)) // Stack inner nếu có.
            {
                sb.AppendLine("Stack trace (inner):"); // Nhãn.
                sb.AppendLine(inner.StackTrace); // Stack inner.
            }
        }

        sb.AppendLine("==============================="); // Viền đóng.
        SafeWriteLine(sb.ToString()); // Ghi một lần ra console có khóa.
    }

    // Nhiệm vụ: ghi một dòng ra Console không bị cắt ngang bởi luồng khác. Cách làm: lock ConsoleLock.
    private static void SafeWriteLine(string text)
    {
        lock (ConsoleLock) // Đảm bảo atomic một dòng hoàn chỉnh.
        {
            Console.WriteLine(text); // In ra stdout.
        }
    }
}
