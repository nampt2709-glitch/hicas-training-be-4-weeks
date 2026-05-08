using DotNetEnv;

namespace CommentAPI.Configuration;

/// <summary>
/// Nạp file .env cho CommentAPI; file .env gốc solution (có docker-compose.yml) nạp trước để có MSSQL_SA_PASSWORD.
/// </summary>
public static class EnvLoader
{
    private static readonly LoadOptions LoadClobber = new(
        setEnvVars: true,
        clobberExistingVars: true);

    /// <summary>
    /// Nạp .env trước khi gọi WebApplication.CreateBuilder: host đọc biến môi trường (ConnectionStrings__*) đúng lúc.
    /// </summary>
    public static void LoadEnvFilesBeforeHost()
    {
        foreach (var path in DiscoverPathsBeforeHost())
        {
            Env.Load(path, LoadClobber);
        }
    }

    /// <summary>
    /// Đọc file .env trong ContentRootPath của project (ghi đè khóa trùng).
    /// </summary>
    public static void LoadEnvFile(string contentRootPath)
    {
        var envFilePath = Path.Combine(contentRootPath, ".env");
        if (!File.Exists(envFilePath))
        {
            return;
        }

        Env.Load(envFilePath, LoadClobber);
    }

    private static IEnumerable<string> DiscoverPathsBeforeHost()
    {
        var result = new List<string>();

        void TryAdd(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            var full = Path.GetFullPath(path);
            if (result.Any(x => string.Equals(x, full, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            result.Add(full);
        }

        void TryPrepend(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            var full = Path.GetFullPath(path);
            if (result.Any(x => string.Equals(x, full, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            result.Insert(0, full);
        }

        var cwd = Directory.GetCurrentDirectory();

        foreach (var start in new[] { cwd, AppContext.BaseDirectory })
        {
            var solutionDir = FindDirUp(start, "docker-compose.yml");
            if (solutionDir != null)
            {
                TryPrepend(Path.Combine(solutionDir, ".env"));
                break;
            }
        }

        TryAdd(Path.Combine(cwd, ".env"));
        TryAdd(Path.Combine(cwd, "CommentAPI", ".env"));

        return result;
    }

    private static string? FindDirUp(string startDir, string fileName)
    {
        var dir = startDir;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, fileName)))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }

        return null;
    }
}
