using DotNetEnv;

namespace CommentAPI.Configuration;

/// <summary>
/// Nạp duy nhất một file .env cho CommentAPI để ghi đè cấu hình nhạy cảm (connection strings, v.v.).
/// </summary>
public static class EnvLoader
{
    /// <summary>
    /// Đọc file .env trong ContentRootPath của project hiện tại.
    /// </summary>
    public static void LoadEnvFile(string contentRootPath)
    {
        var envFilePath = Path.Combine(contentRootPath, ".env");
        if (!File.Exists(envFilePath))
        {
            return; // Không có .env thì giữ nguyên cấu hình mặc định khác (appsettings/biến hệ thống).
        }

        Env.Load(envFilePath); // Nạp biến môi trường từ một file duy nhất theo yêu cầu.
    }
}
