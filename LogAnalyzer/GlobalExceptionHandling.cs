using System.Text;

namespace LogAnalyzer;

// Đăng ký xử lý exception toàn cục: ngoài try/catch thông thường, luồng nền hoặc Task không được await.
// Chuỗi in ra console dùng tiếng Anh (giao diện người dùng); comment trong file vẫn tiếng Việt có dấu.
public static class GlobalExceptionHandling
{
    private static readonly object ConsoleLock = new();

    public static void Register()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            WriteExceptionBlock("Unhandled exception (AppDomain.UnhandledException)", ex, includeStackTrace: true);
        }
        else
        {
            SafeWriteLine($"Unhandled exception (not an Exception object): {e.ExceptionObject}");
        }

        if (e.IsTerminating)
        {
            SafeWriteLine("The application is terminating due to an unhandled exception.");
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteExceptionBlock("Unobserved task exception (TaskScheduler.UnobservedTaskException)", e.Exception, includeStackTrace: true);
        e.SetObserved();
    }

    public static void WriteExceptionBlock(string title, Exception ex, bool includeStackTrace)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("========== EXCEPTION ==========");
        sb.AppendLine(title);
        sb.AppendLine($"Type: {ex.GetType().FullName}");
        sb.AppendLine($"Message: {ex.Message}");

        if (includeStackTrace && !string.IsNullOrWhiteSpace(ex.StackTrace))
        {
            sb.AppendLine("Stack trace:");
            sb.AppendLine(ex.StackTrace);
        }

        if (ex.InnerException is { } inner)
        {
            sb.AppendLine("--- Inner exception ---");
            sb.AppendLine($"Type: {inner.GetType().FullName}");
            sb.AppendLine($"Message: {inner.Message}");
            if (includeStackTrace && !string.IsNullOrWhiteSpace(inner.StackTrace))
            {
                sb.AppendLine("Stack trace (inner):");
                sb.AppendLine(inner.StackTrace);
            }
        }

        sb.AppendLine("===============================");
        SafeWriteLine(sb.ToString());
    }

    private static void SafeWriteLine(string text)
    {
        lock (ConsoleLock)
        {
            Console.WriteLine(text);
        }
    }
}
