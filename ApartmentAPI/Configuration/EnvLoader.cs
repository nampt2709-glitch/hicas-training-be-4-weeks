using DotNetEnv; // Env.Load từ file .env.

namespace ApartmentAPI.Configuration;

// Nạp .env: tìm thư mục project (kể cả khi cwd là bin/ hoặc thư mục solution), nhiều file — file sau ghi đè khóa trùng (cùng SQL Docker CommentAPI).
public static class EnvLoader
{ // Mở khối EnvLoader.
    // clobberExistingVars=true: biến môi trường đã có vẫn bị ghi đè bởi file .env sau cùng trong thứ tự Discover.
    private static readonly LoadOptions LoadClobber = new(
        setEnvVars: true,
        clobberExistingVars: true);

    // Host build trước: nạp mọi path discovered — dùng trong Program trước WebApplication.CreateBuilder nếu cần.
    public static void LoadEnvFilesBeforeHost()
    { // Mở khối LoadEnvFilesBeforeHost.
        foreach (var path in DiscoverEnvPaths())
        { // BƯỚC 1 — Tuần tự load; file sau override key trùng nhờ LoadClobber.
            Env.Load(path, LoadClobber);
        }
    } // Kết thúc LoadEnvFilesBeforeHost.

    // Nạp đúng một .env dưới contentRoot nếu tồn tại — dùng khi biết chắc thư mục wwwroot/content root.
    public static void LoadEnvFile(string contentRootPath)
    { // Mở khối LoadEnvFile.
        var envFilePath = Path.Combine(contentRootPath, ".env");
        if (!File.Exists(envFilePath))
        { // TRƯỜNG HỢP A — Không có file → im lặng bỏ qua.
            return;
        }

        Env.Load(envFilePath, LoadClobber);
    } // Kết thúc LoadEnvFile.

    // Thứ tự nạp: từ chung đến riêng — file sau ghi đè ConnectionStrings nếu trùng khóa.
    private static IEnumerable<string> DiscoverEnvPaths()
    { // Mở khối DiscoverEnvPaths.
        var result = new List<string>();

        void TryAdd(string path)
        { // Hàm cục bộ: thêm path tuyệt đối unique nếu file tồn tại.
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

        // BƯỚC 1 — cwd và cwd/ApartmentAPI/.env (khi chạy từ solution root).
        var cwd = Directory.GetCurrentDirectory();
        TryAdd(Path.Combine(cwd, ".env"));
        TryAdd(Path.Combine(cwd, "ApartmentAPI", ".env"));

        // BƯỚC 2 — Đi ngược từ cwd tìm thư mục chứa ApartmentAPI.csproj.
        var projectDir = FindDirectoryUpwardsContaining(cwd, "ApartmentAPI.csproj");
        if (projectDir != null)
        {
            TryAdd(Path.Combine(projectDir, ".env"));
        }

        // BƯỚC 3 — Đi ngược từ BaseDirectory (thường là bin) để tìm project .env khi host chạy assembly.
        projectDir = FindDirectoryUpwardsContaining(AppContext.BaseDirectory, "ApartmentAPI.csproj");
        if (projectDir != null)
        {
            TryAdd(Path.Combine(projectDir, ".env"));
        }

        return result;
    } // Kết thúc DiscoverEnvPaths.

    // BFS ngược thư mục cha cho đến khi thấy fileName hoặc hết cây.
    private static string? FindDirectoryUpwardsContaining(string startDir, string fileName)
    { // Mở khối FindDirectoryUpwardsContaining.
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
    } // Kết thúc FindDirectoryUpwardsContaining.
} // Kết thúc EnvLoader.
