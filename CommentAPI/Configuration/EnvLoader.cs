using DotNetEnv;

namespace CommentAPI.Configuration;

/// <summary>
/// Nạp file .env cho CommentAPI để ghi đè cấu hình nhạy cảm (connection strings, v.v.).
/// </summary>
public static class EnvLoader
{
    /// <summary>
    /// Nạp .env trước khi gọi WebApplication.CreateBuilder: host đọc biến môi trường (ConnectionStrings__*) đúng lúc.
    /// Thử thư mục hiện tại và CommentAPI/.env khi chạy từ root solution.
    /// </summary>
    public static void LoadEnvFilesBeforeHost()
    {
        var cwd = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.Combine(cwd, ".env"),
            Path.Combine(cwd, "CommentAPI", ".env"),
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            Env.Load(path); // Ghi đè biến trùng tên theo thứ tự file (file sau thắng nếu DotNetEnv merge mặc định).
        }
    }

    /// <summary>
    /// Đọc file .env trong ContentRootPath của project hiện tại (dự phòng sau khi host đã tạo).
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
