using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace LogAnalyzer;

// Điểm vào: cấu hình DI, sau đó menu console (các thao tác dùng interface đã đăng ký).
public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        GlobalExceptionHandling.Register();

        var services = new ServiceCollection();
        services.AddSingleton<IFileReader, FileReaderService>();
        services.AddSingleton<IResultWriter, ResultWriterService>();
        services.AddSingleton<ILogGenerator, LogGeneratorService>();
        services.AddSingleton<IBenchmarkRunner, BenchmarkRunnerService>();

        await using var provider = services.BuildServiceProvider();

        var benchmarkRunner = provider.GetRequiredService<IBenchmarkRunner>();
        var resultWriter = provider.GetRequiredService<IResultWriter>();
        var logGenerator = provider.GetRequiredService<ILogGenerator>();

        try
        {
            await RunMenuLoopAsync(benchmarkRunner, resultWriter, logGenerator);
        }
        catch (Exception ex)
        {
            GlobalExceptionHandling.WriteExceptionBlock("Fatal error — exiting", ex, includeStackTrace: true);
            Environment.ExitCode = 1;
        }
    }

    private static async Task RunMenuLoopAsync(
        IBenchmarkRunner benchmarkRunner,
        IResultWriter resultWriter,
        ILogGenerator logGenerator)
    {
        while (true)
        {
            try
            {
                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine("LogAnalyzer");
                Console.WriteLine("========================================");
                Console.WriteLine("1. Analyze existing file");
                Console.WriteLine("2. Generate error log only");
                Console.WriteLine("3. Generate error log and analyze");
                Console.WriteLine("0. Exit");
                Console.Write("Choose: ");

                var choice = (Console.ReadLine() ?? string.Empty).Trim();

                if (choice.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                    choice.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                switch (choice)
                {
                    case "1":
                        await AnalyzeExistingFileAsync(benchmarkRunner, resultWriter);
                        break;

                    case "2":
                        await GenerateOnlyAsync(logGenerator);
                        break;

                    case "3":
                        await GenerateAndAnalyzeAsync(benchmarkRunner, resultWriter, logGenerator);
                        break;

                    default:
                        Console.WriteLine("Invalid choice.");
                        continue;
                }

                Console.WriteLine();
                Console.WriteLine("Press Enter to continue, or type exit to quit.");
                var next = (Console.ReadLine() ?? string.Empty).Trim();
                if (next.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                GlobalExceptionHandling.WriteExceptionBlock("Error in menu (press Enter to continue)", ex, includeStackTrace: true);
                Console.WriteLine("Press Enter to return to menu...");
                Console.ReadLine();
            }
        }
    }

    private static async Task AnalyzeExistingFileAsync(
        IBenchmarkRunner benchmarkRunner,
        IResultWriter resultWriter)
    {
        Console.Write("Enter file path: ");
        var inputPath = (Console.ReadLine() ?? string.Empty).Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            Console.WriteLine("No file selected.");
            return;
        }

        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"File not found: {inputPath}");
            return;
        }

        var mode = Analyzer.DetectMode(Path.GetFileName(inputPath));

        Console.WriteLine();
        Console.WriteLine("Starting analysis...");
        Console.WriteLine($"Mode: {mode}");
        Console.WriteLine($"File: {inputPath}");
        Console.WriteLine();

        var report = await benchmarkRunner.RunAsync(
            inputPath,
            mode,
            message => Console.WriteLine(message));

        PrintPerformanceTableToConsole(report);

        Console.WriteLine();
        Console.WriteLine("Writing result file...");

        var resultPath = resultWriter.Write(report);

        Console.WriteLine("Done. Result saved to:");
        Console.WriteLine(resultPath);
    }

    private static void PrintPerformanceTableToConsole(BenchmarkReport report)
    {
        Console.WriteLine();
        Console.WriteLine("===== PERFORMANCE =====");
        Console.WriteLine($"{"Mode",-30}{"Time(ms)",12}");
        foreach (var run in report.Runs)
        {
            Console.WriteLine($"{run.Label,-30}{run.ElapsedMilliseconds,12}");
        }
    }

    private static Task GenerateOnlyAsync(ILogGenerator logGenerator)
    {
        Console.WriteLine();
        Console.WriteLine("Generating error log...");
        Console.WriteLine("Target: 1,000,000 lines");
        Console.WriteLine("Selected types: 100 random items from 200 predefined error terms");
        Console.WriteLine();

        var generatedPath = logGenerator.Generate(
            lineCount: 1_000_000,
            selectedTypeCount: 100,
            progress: message => Console.WriteLine(message));

        Console.WriteLine();
        Console.WriteLine("Generated error log:");
        Console.WriteLine(generatedPath);
        return Task.CompletedTask;
    }

    private static async Task GenerateAndAnalyzeAsync(
        IBenchmarkRunner benchmarkRunner,
        IResultWriter resultWriter,
        ILogGenerator logGenerator)
    {
        Console.WriteLine();
        Console.WriteLine("Generating error log...");
        Console.WriteLine("Target: 1,000,000 lines");
        Console.WriteLine("Selected types: 100 random items from 200 predefined error terms");
        Console.WriteLine();

        var generatedPath = logGenerator.Generate(
            lineCount: 1_000_000,
            selectedTypeCount: 100,
            progress: message => Console.WriteLine(message));

        Console.WriteLine();
        Console.WriteLine("Generated error log:");
        Console.WriteLine(generatedPath);
        Console.WriteLine();

        var mode = Analyzer.DetectMode(Path.GetFileName(generatedPath));

        Console.WriteLine("Starting analysis...");
        Console.WriteLine($"Mode: {mode}");
        Console.WriteLine($"File: {generatedPath}");
        Console.WriteLine();

        var report = await benchmarkRunner.RunAsync(
            generatedPath,
            mode,
            message => Console.WriteLine(message));

        PrintPerformanceTableToConsole(report);

        Console.WriteLine();
        Console.WriteLine("Writing result file...");

        var resultPath = resultWriter.Write(report);

        Console.WriteLine("Done. Result saved to:");
        Console.WriteLine(resultPath);
    }
}
