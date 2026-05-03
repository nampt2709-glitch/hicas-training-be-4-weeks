using System.Text; // Encoding.UTF8 cho Console.
using Microsoft.Extensions.DependencyInjection; // ServiceCollection, DI.

namespace LogAnalyzer; // Không gian tên ứng dụng console.

// Lớp tĩnh chứa điểm vào Main và vòng menu điều khiển LogAnalyzer.
public static class Program
{
    // Nhiệm vụ: khởi tạo UTF-8, DI, chạy menu hoặc bắt lỗi fatal. Cách làm: BuildServiceProvider rồi RunMenuLoopAsync trong try/catch.
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8; // Hiển thị Unicode (tiếng Việt) đúng trên console.

        GlobalExceptionHandling.Register(); // Đăng ký handler exception toàn cục.

        var services = new ServiceCollection(); // Tạo bộ đăng ký DI.
        services.AddSingleton<IFileReader, FileReaderService>(); // Một instance đọc file cho cả app.
        services.AddSingleton<IResultWriter, ResultWriterService>(); // Một instance ghi kết quả.
        services.AddSingleton<ILogGenerator, LogGeneratorService>(); // Một instance sinh log mẫu.
        services.AddSingleton<IBenchmarkRunner, BenchmarkRunnerService>(); // Một instance chạy benchmark.

        await using var provider = services.BuildServiceProvider(); // Xây provider; await using gọi DisposeAsync khi hết scope.

        var benchmarkRunner = provider.GetRequiredService<IBenchmarkRunner>(); // Lấy runner (throw nếu thiếu đăng ký).
        var resultWriter = provider.GetRequiredService<IResultWriter>(); // Lấy writer kết quả.
        var logGenerator = provider.GetRequiredService<ILogGenerator>(); // Lấy generator log.

        try // Bọc toàn bộ menu để bắt lỗi không xử lý ở Main.
        {
            await RunMenuLoopAsync(benchmarkRunner, resultWriter, logGenerator); // Chạy vòng lặp menu đến khi thoát.
        }
        catch (Exception ex) // Mọi exception thoát khỏi menu loop.
        {
            GlobalExceptionHandling.WriteExceptionBlock("Fatal error — exiting", ex, includeStackTrace: true); // In chi tiết lỗi.
            Environment.ExitCode = 1; // Báo cho shell mã thoát khác 0.
        }
    }

    // Nhiệm vụ: hiển thị menu lặp lại, phân nhánh chức năng, bắt lỗi từng lần chọn. Cách làm: while(true) + switch + try/catch nội bộ.
    private static async Task RunMenuLoopAsync(
        IBenchmarkRunner benchmarkRunner, // Dịch vụ benchmark.
        IResultWriter resultWriter, // Dịch vụ ghi file kết quả.
        ILogGenerator logGenerator) // Dịch vụ sinh log.
    {
        while (true) // Lặp vô hạn cho đến break (thoát hoặc exit).
        {
            try // Mỗi vòng menu có try riêng để không crash cả app.
            {
                Console.WriteLine(); // Dòng trống cho dễ đọc.
                Console.WriteLine("========================================"); // Viền trang trí.
                Console.WriteLine("LogAnalyzer"); // Tiêu đề app.
                Console.WriteLine("========================================"); // Viền đóng.
                Console.WriteLine("1. Analyze existing file"); // Mục 1.
                Console.WriteLine("2. Generate error log only"); // Mục 2.
                Console.WriteLine("3. Generate error log and analyze"); // Mục 3.
                Console.WriteLine("0. Exit"); // Thoát.
                Console.Write("Choose: "); // Nhắc nhập.

                var choice = (Console.ReadLine() ?? string.Empty).Trim(); // Đọc dòng, null → rỗng, trim.

                if (choice.Equals("0", StringComparison.OrdinalIgnoreCase) || // Người dùng chọn 0.
                    choice.Equals("exit", StringComparison.OrdinalIgnoreCase)) // Hoặc gõ exit.
                {
                    break; // Thoát vòng menu.
                }

                switch (choice) // Phân nhánh theo lựa chọn.
                {
                    case "1": // Phân tích file có sẵn.
                        await AnalyzeExistingFileAsync(benchmarkRunner, resultWriter); // Gọi luồng phân tích.
                        break; // Thoát switch.

                    case "2": // Chỉ sinh log.
                        await GenerateOnlyAsync(logGenerator); // Gọi sinh file.
                        break;

                    case "3": // Sinh rồi phân tích.
                        await GenerateAndAnalyzeAsync(benchmarkRunner, resultWriter, logGenerator); // Hai bước liên tiếp.
                        break;

                    default: // Lựa chọn không hợp lệ.
                        Console.WriteLine("Invalid choice."); // Thông báo.
                        continue; // Quay lại đầu vòng while, không chờ Enter.
                }

                Console.WriteLine(); // Dòng trống sau chức năng.
                Console.WriteLine("Press Enter to continue, or type exit to quit."); // Hướng dẫn.
                var next = (Console.ReadLine() ?? string.Empty).Trim(); // Đọc lệnh tiếp theo.
                if (next.Equals("exit", StringComparison.OrdinalIgnoreCase)) // Muốn thoát hẳn.
                {
                    break; // Thoát while.
                }
            }
            catch (Exception ex) // Lỗi trong một lần chọn menu.
            {
                GlobalExceptionHandling.WriteExceptionBlock("Error in menu (press Enter to continue)", ex, includeStackTrace: true); // In lỗi.
                Console.WriteLine("Press Enter to return to menu..."); // Hướng dẫn.
                Console.ReadLine(); // Chờ người dùng xác nhận.
            }
        }
    }

    // Nhiệm vụ: nhập đường file, chạy benchmark, in tóm tắt, ghi Results. Cách làm: validate path → DetectMode → RunAsync → Write.
    private static async Task AnalyzeExistingFileAsync(
        IBenchmarkRunner benchmarkRunner, // Runner đo hiệu năng.
        IResultWriter resultWriter) // Writer lưu báo cáo.
    {
        Console.Write("Enter file path: "); // Yêu cầu đường dẫn.
        var inputPath = (Console.ReadLine() ?? string.Empty).Trim().Trim('"'); // Đọc và bỏ dấu ngoặc kép nếu paste từ Explorer.

        if (string.IsNullOrWhiteSpace(inputPath)) // Không nhập gì.
        {
            Console.WriteLine("No file selected."); // Thông báo.
            return; // Kết thúc sớm.
        }

        if (!File.Exists(inputPath)) // File không tồn tại.
        {
            Console.WriteLine($"File not found: {inputPath}"); // Báo đường dẫn.
            return;
        }

        var mode = Analyzer.DetectMode(Path.GetFileName(inputPath)); // Quyết định Word/Error từ tên file.

        Console.WriteLine(); // Dòng trống.
        Console.WriteLine("Starting analysis..."); // Thông báo bắt đầu.
        Console.WriteLine($"Mode: {mode}"); // In chế độ.
        Console.WriteLine($"File: {inputPath}"); // In file.
        Console.WriteLine();

        var report = await benchmarkRunner.RunAsync( // Chạy đủ warm-up + đọc + đếm.
            inputPath, // Đường file.
            mode, // Chế độ phân tích.
            message => Console.WriteLine(message)); // Callback log tiến độ.

        PrintBenchmarkSummaryToConsole(report); // In bảng tóm tắt ra console.

        Console.WriteLine();
        Console.WriteLine("Writing result file..."); // Báo đang ghi.

        var resultPath = resultWriter.Write(report); // Ghi file Results/*.txt.

        Console.WriteLine("Done. Result saved to:"); // Thông báo xong.
        Console.WriteLine(resultPath); // In đường file kết quả.
    }

    // Nhiệm vụ: in read runs, count runs, thống kê và danh sách tần suất. Cách làm: foreach + nhánh Mode Word/Error.
    private static void PrintBenchmarkSummaryToConsole(BenchmarkReport report)
    {
        Console.WriteLine(); // Dòng trống.
        Console.WriteLine("===== THỜI GIAN ĐỌC FILE (ms) ====="); // Tiêu đề khối đọc.
        Console.WriteLine($"{"Thao tác",-45}{"Thời gian (ms)",15}"); // Header hai cột.
        foreach (var run in report.ReadRuns) // Sync rồi Async.
        {
            Console.WriteLine($"{run.Label,-45}{run.ElapsedMilliseconds,15}"); // Một dòng kết quả đọc.
        }

        Console.WriteLine();
        Console.WriteLine("===== THỜI GIAN ĐẾM TẦN SUẤT (ms) ====="); // Tiêu đề khối đếm.
        Console.WriteLine($"{"Phương pháp",-45}{"Thời gian (ms)",15}"); // Header.
        foreach (var run in report.CountRuns) // Ba phương pháp đếm.
        {
            Console.WriteLine($"{run.Label,-45}{run.ElapsedMilliseconds,15}"); // Nhãn + ms.
        }

        Console.WriteLine();
        if (report.Mode == AnalysisMode.Word) // Thống kê theo từ.
        {
            Console.WriteLine($"Tổng số từ (lần xuất hiện): {report.TotalOccurrences:n0}"); // Tổng occurrences.
            Console.WriteLine($"Tổng số loại từ khác nhau: {report.DistinctTypeCount:n0}"); // Số khóa duy nhất.
        }
        else // Thống kê theo lỗi.
        {
            Console.WriteLine($"Tổng số lỗi (lần xuất hiện): {report.TotalOccurrences:n0}");
            Console.WriteLine($"Tổng số loại lỗi khác nhau: {report.DistinctTypeCount:n0}");
        }

        Console.WriteLine();
        Console.WriteLine("===== DANH SÁCH TẦN SUẤT ====="); // Tiêu đề danh sách.
        if (report.FrequencyItems.Count == 0) // Không có mục nào.
        {
            Console.WriteLine(report.Mode == AnalysisMode.Word ? "Không có từ nào." : "Không có thuật ngữ lỗi nào."); // Thông báo rỗng.
        }
        else // Có dữ liệu.
        {
            for (var i = 0; i < report.FrequencyItems.Count; i++) // Duyệt chỉ số.
            {
                var item = report.FrequencyItems[i]; // Lấy phần tử.
                Console.WriteLine($"{i + 1,4}. {item.Name} — {item.Count}"); // In STT, tên, count.
            }
        }
    }

    // Nhiệm vụ: chỉ sinh file log lỗi 1M dòng. Cách làm: gọi logGenerator.Generate rồi in đường dẫn; trả CompletedTask.
    private static Task GenerateOnlyAsync(ILogGenerator logGenerator)
    {
        Console.WriteLine(); // Dòng trống.
        Console.WriteLine("Generating error log..."); // Thông báo.
        Console.WriteLine("Target: 1,000,000 lines"); // Quy mô.
        Console.WriteLine("Selected types: 100 random items from 200 predefined error terms"); // Mô tả catalog.
        Console.WriteLine();

        var generatedPath = logGenerator.Generate( // Sinh file.
            lineCount: 1_000_000, // Một triệu dòng.
            selectedTypeCount: 100, // Trăm loại lỗi.
            progress: message => Console.WriteLine(message)); // In tiến độ.

        Console.WriteLine();
        Console.WriteLine("Generated error log:"); // Tiêu đề.
        Console.WriteLine(generatedPath); // Đường file.
        return Task.CompletedTask; // Không có await thật → task hoàn thành ngay.
    }

    // Nhiệm vụ: sinh log rồi chạy benchmark trên file vừa tạo. Cách làm: Generate → DetectMode → RunAsync → Print → Write.
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

        var mode = Analyzer.DetectMode(Path.GetFileName(generatedPath)); // Error vì tên ErrorLog_*.log.

        Console.WriteLine("Starting analysis...");
        Console.WriteLine($"Mode: {mode}");
        Console.WriteLine($"File: {generatedPath}");
        Console.WriteLine();

        var report = await benchmarkRunner.RunAsync(
            generatedPath,
            mode,
            message => Console.WriteLine(message));

        PrintBenchmarkSummaryToConsole(report);

        Console.WriteLine();
        Console.WriteLine("Writing result file...");

        var resultPath = resultWriter.Write(report);

        Console.WriteLine("Done. Result saved to:");
        Console.WriteLine(resultPath);
    }
}
