using System.Text;

namespace LogAnalyzer;

// Sinh file log mẫu lớn (cùng logic trước khi tách service).
public sealed class LogGeneratorService : ILogGenerator
{
    public string Generate(
        int lineCount = 1_000_000,
        int selectedTypeCount = 100,
        Action<string>? progress = null)
    {
        if (lineCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineCount));
        }

        if (selectedTypeCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedTypeCount));
        }

        var logsDir = Path.Combine(AppContext.BaseDirectory, "Logs");
        Directory.CreateDirectory(logsDir);

        var fileName = $"ErrorLog_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log";
        var outputPath = Path.Combine(logsDir, fileName);

        var random = Random.Shared;
        var selectedTypes = ErrorCatalog.GetRandomSample(selectedTypeCount, random);

        var modules = new[]
        {
            "Auth", "Api", "Db", "Cache", "UI",
            "Worker", "Gateway", "Scheduler", "Storage", "Report"
        };

        var baseTime = DateTime.UtcNow;
        var checkpoint = Math.Max(1, lineCount / 20);

        using var stream = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 1 << 20);

        using var writer = new StreamWriter(stream, Encoding.UTF8);

        progress?.Invoke($"Writing to: {outputPath}");

        for (var i = 0; i < lineCount; i++)
        {
            var timestamp = baseTime.AddMilliseconds(i).ToString("yyyyMMdd HH:mm:ss.fff");
            var errorType = selectedTypes[random.Next(selectedTypes.Length)];
            var module = modules[random.Next(modules.Length)];
            var code = random.Next(1000, 9999);

            writer.Write(timestamp);
            writer.Write(" [ERROR] ");
            writer.Write(errorType);
            writer.Write(" Module=");
            writer.Write(module);
            writer.Write(" Code=");
            writer.Write(code);
            writer.WriteLine();

            if ((i + 1) % checkpoint == 0 || i == lineCount - 1)
            {
                progress?.Invoke($"Generated {i + 1:n0}/{lineCount:n0} lines...");
            }
        }

        return outputPath;
    }
}
