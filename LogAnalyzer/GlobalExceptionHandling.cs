using System.Text;

namespace LogAnalyzer;

/// <summary>
/// Dang ky bat exception "toan cuc": ngoai vong try/catch thuong, luong nen, hoac task khong duoc await.
/// Giup hoc sinh / nguoi dung thay loi ro rang thay vi crash im lang.
/// </summary>
public static class GlobalExceptionHandling
{
    // Khoa don gian de nhieu luong khong in xen ke len console khi in loi.
    private static readonly object ConsoleLock = new();

    /// <summary>
    /// Goi mot lan luc khoi dong (truoc khi chay logic chinh): gan su kien AppDomain va TaskScheduler.
    /// </summary>
    public static void Register()
    {
        // Bat exception tren luong khong co try/catch; sau handler process van co the ket thuc (hanh vi mac dinh runtime).
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        // Bat exception trong Task bi "bo quen" (khong await, khong .Wait); SetObserved tranh lan truyen them o mot so phien ban runtime.
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // e.ExceptionObject co the khong phai Exception (hiem); in an toan ra console.
        if (e.ExceptionObject is Exception ex)
        {
            WriteExceptionBlock("Loi KHONG xu ly (AppDomain.UnhandledException)", ex, includeStackTrace: true);
        }
        else
        {
            SafeWriteLine($"Loi KHONG xu ly (khong phai Exception): {e.ExceptionObject}");
        }

        if (e.IsTerminating)
        {
            SafeWriteLine("Ung dung sap ket thuc do exception chua xu ly.");
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Ghi lai toan bo AggregateException (neu co nhieu loi long nhau).
        WriteExceptionBlock("Task khong duoc quan sat (UnobservedTaskException)", e.Exception, includeStackTrace: true);
        e.SetObserved();
    }

    /// <summary>
    /// In exception thong nhat: tieu de, loai, message, stack (tuy chon). Dung lock de tranh xen chu console khi nhieu luong.
    /// </summary>
    public static void WriteExceptionBlock(string title, Exception ex, bool includeStackTrace)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("========== EXCEPTION ==========");
        sb.AppendLine(title);
        sb.AppendLine($"Loai: {ex.GetType().FullName}");
        sb.AppendLine($"Message: {ex.Message}");

        if (includeStackTrace && !string.IsNullOrWhiteSpace(ex.StackTrace))
        {
            sb.AppendLine("Stack trace:");
            sb.AppendLine(ex.StackTrace);
        }

        if (ex.InnerException is { } inner)
        {
            sb.AppendLine("--- Inner exception ---");
            sb.AppendLine($"Loai: {inner.GetType().FullName}");
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
