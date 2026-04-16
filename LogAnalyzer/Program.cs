using System.Text;

namespace LogAnalyzer;

/// <summary>
/// Entry point: infinite console menu until the user exits.
/// Three flows: analyze an existing file, generate error log only, or generate then analyze.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        // UTF-8 so Vietnamese and special characters print correctly on the console.
        Console.OutputEncoding = Encoding.UTF8;

        // Register process-wide handlers (background threads, unobserved tasks) before any work runs.
        GlobalExceptionHandling.Register();

        try
        {
            await RunMenuLoopAsync();
        }
        catch (Exception ex)
        {
            // Any exception that escapes the menu loop is treated as fatal; set a non-zero exit code.
            GlobalExceptionHandling.WriteExceptionBlock("Fatal error — exiting", ex, includeStackTrace: true);
            Environment.ExitCode = 1;
        }
    }

    /// <summary>
    /// Menu loop isolated so <see cref="Main"/> can wrap it in an outer try/catch.
    /// </summary>
    private static async Task RunMenuLoopAsync()
    {
        // After each successful action the user can return to the menu or type exit.
        while (true)
        {
            try
            {
                // --- Show menu and read choice ---
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

                // Exit immediately on 0 or "exit" (case-insensitive).
                if (choice.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                    choice.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                // --- Branch: each case runs a different pipeline ---
                switch (choice)
                {
                    case "1":
                        // User path: read file, run 6-way benchmark, write report.
                        await AnalyzeExistingFileAsync();
                        break;

                    case "2":
                        // Generate a large sample log (1M lines) for manual testing or option 1.
                        await GenerateOnlyAsync();
                        break;

                    case "3":
                        // Generate sample log then run the same benchmark pipeline as option 1.
                        await GenerateAndAnalyzeAsync();
                        break;

                    default:
                        Console.WriteLine("Invalid choice.");
                        continue;
                }

                // --- After one action: Enter returns to menu, exit quits ---
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
                // Keep the app alive: print full details (including stack) then return to the menu.
                GlobalExceptionHandling.WriteExceptionBlock("Error in menu (press Enter to continue)", ex, includeStackTrace: true);
                Console.WriteLine("Press Enter to return to menu...");
                Console.ReadLine();
            }
        }
    }

    private static async Task AnalyzeExistingFileAsync()
    {
        // --- Ask for file path ---
        Console.Write("Enter file path: ");
        // Trim and strip quotes (Windows paths copied as "C:\...").
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

        // --- Infer analysis mode from file name ("ErrorLog" => error-term catalog mode) ---
        var mode = Analyzer.DetectMode(Path.GetFileName(inputPath));

        Console.WriteLine();
        Console.WriteLine("Starting analysis...");
        Console.WriteLine($"Mode: {mode}");
        Console.WriteLine($"File: {inputPath}");
        Console.WriteLine();

        // --- Benchmark: sync/async read × Sequential / Parallel.ForEach / PLINQ ---
        var report = await BenchmarkRunner.RunAsync(
            inputPath,
            mode,
            message => Console.WriteLine(message));

        Console.WriteLine();
        Console.WriteLine("Writing result file...");

        // --- Write timings + top 50 to Results folder ---
        var resultPath = ResultWriter.Write(report);

        Console.WriteLine("Done. Result saved to:");
        Console.WriteLine(resultPath);
    }

    private static Task GenerateOnlyAsync()
    {
        Console.WriteLine();
        Console.WriteLine("Generating error log...");
        Console.WriteLine("Target: 1,000,000 lines");
        Console.WriteLine("Selected types: 100 random items from 200 predefined error terms");
        Console.WriteLine();

        // Synthetic log: timestamp, [ERROR], random error type from catalog, module, code.
        var generatedPath = LogGenerator.Generate(
            lineCount: 1_000_000,
            selectedTypeCount: 100,
            progress: message => Console.WriteLine(message));

        Console.WriteLine();
        Console.WriteLine("Generated error log:");
        Console.WriteLine(generatedPath);
        return Task.CompletedTask;
    }

    private static async Task GenerateAndAnalyzeAsync()
    {
        Console.WriteLine();
        Console.WriteLine("Generating error log...");
        Console.WriteLine("Target: 1,000,000 lines");
        Console.WriteLine("Selected types: 100 random items from 200 predefined error terms");
        Console.WriteLine();

        // Step 1: create log; name contains "ErrorLog" so DetectMode picks Error mode.
        var generatedPath = LogGenerator.Generate(
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

        // Step 2: same benchmark path as AnalyzeExistingFileAsync.
        var report = await BenchmarkRunner.RunAsync(
            generatedPath,
            mode,
            message => Console.WriteLine(message));

        Console.WriteLine();
        Console.WriteLine("Writing result file...");

        var resultPath = ResultWriter.Write(report);

        Console.WriteLine("Done. Result saved to:");
        Console.WriteLine(resultPath);
    }
}
