using DotNetEnv;

namespace ApartmentAPI.Configuration;

// Nạp .env: tìm thư mục project (kể cả khi cwd là bin/ hoặc thư mục solution), nhiều file — file sau ghi đè khóa trùng (cùng SQL Docker CommentAPI).
public static class EnvLoader
{
    private static readonly LoadOptions LoadClobber = new(
        setEnvVars: true,
        clobberExistingVars: true);

    public static void LoadEnvFilesBeforeHost()
    {
        foreach (var path in DiscoverEnvPaths())
        {
            Env.Load(path, LoadClobber);
        }
    }

    public static void LoadEnvFile(string contentRootPath)
    {
        var envFilePath = Path.Combine(contentRootPath, ".env");
        if (!File.Exists(envFilePath))
        {
            return;
        }

        Env.Load(envFilePath, LoadClobber);
    }

    // Thứ tự nạp: từ chung đến riêng — file sau ghi đè ConnectionStrings nếu trùng khóa.
    private static IEnumerable<string> DiscoverEnvPaths()
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

        var cwd = Directory.GetCurrentDirectory();
        TryAdd(Path.Combine(cwd, ".env"));
        TryAdd(Path.Combine(cwd, "ApartmentAPI", ".env"));

        var projectDir = FindDirectoryUpwardsContaining(cwd, "ApartmentAPI.csproj");
        if (projectDir != null)
        {
            TryAdd(Path.Combine(projectDir, ".env"));
        }

        projectDir = FindDirectoryUpwardsContaining(AppContext.BaseDirectory, "ApartmentAPI.csproj");
        if (projectDir != null)
        {
            TryAdd(Path.Combine(projectDir, ".env"));
        }

        return result;
    }

    private static string? FindDirectoryUpwardsContaining(string startDir, string fileName)
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
