using DotNetEnv; // Env.Load — nạp file .env vào biến môi trường tiến trình.

namespace CommentAPI.Configuration;

// =============================================================================
// File EnvLoader.cs: phát hiện và nạp .env (solution root trước, rồi cwd/CommentAPI) trước WebApplication.CreateBuilder.
// =============================================================================

// Nạp file .env cho CommentAPI; file .env gốc solution (có docker-compose.yml) nạp trước để có MSSQL_SA_PASSWORD.
public static class EnvLoader
{
    private static readonly LoadOptions LoadClobber = new(
        setEnvVars: true,
        clobberExistingVars: true); // Ghi đè biến đã có — .env sau thắng (cụ thể trong DiscoverPathsBeforeHost).

    // Nạp .env trước khi gọi WebApplication.CreateBuilder: host đọc biến môi trường (ConnectionStrings__*) đúng lúc.
    public static void LoadEnvFilesBeforeHost()
    { // Mở khối LoadEnvFilesBeforeHost.
        // BƯỚC 1 — Duyệt danh sách đường dẫn ưu tiên (solution .env trước) và gọi Env.Load từng file tồn tại.
        foreach (var path in DiscoverPathsBeforeHost())
        {
            Env.Load(path, LoadClobber);
        }
    } // Kết thúc LoadEnvFilesBeforeHost.

    // Đọc file .env trong ContentRootPath của project (ghi đè khóa trùng).
    public static void LoadEnvFile(string contentRootPath)
    { // Mở khối LoadEnvFile.
        // BƯỚC 1 — Nếu .env nằm cạnh csproj — nạp với clobber (Program gọi sau CreateBuilder).
        var envFilePath = Path.Combine(contentRootPath, ".env");
        if (!File.Exists(envFilePath))
        {
            return;
        }

        Env.Load(envFilePath, LoadClobber);
    } // Kết thúc LoadEnvFile.

    private static IEnumerable<string> DiscoverPathsBeforeHost()
    { // Mở khối DiscoverPathsBeforeHost.
        // BƯỚC 1 — Gom danh sách đường dẫn .env duy nhất (không phân biệt hoa thường) theo thứ tự ưu tiên.
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
    } // Kết thúc DiscoverPathsBeforeHost.

    private static string? FindDirUp(string startDir, string fileName)
    { // Mở khối FindDirUp.
        // BƯỚC 1 — Leo thư mục cha từ startDir cho tới khi thấy fileName hoặc hết cây.
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
    } // Kết thúc FindDirUp.
} // Kết thúc lớp EnvLoader.
