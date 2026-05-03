using System.Text;
using LogAnalyzer;
using Xunit;

namespace LogAnalyzer.Tests;

public class GlobalExceptionHandlingTests
{
    // F.I.R.S.T: test cô lập Console output, chạy nhanh.
    // 3A — Arrange: exception có inner exception. Act: gọi WriteExceptionBlock với includeStackTrace=false. Assert: có title/type/message/inner và không có nhãn Stack trace.
    [Fact]
    public void GEH01_WriteExceptionBlock_ShouldWriteExpectedContent_WithoutStackTrace()
    {
        var previousOut = Console.Out;
        var writer = new StringWriter(new StringBuilder());
        Console.SetOut(writer);

        try
        {
            var ex = new InvalidOperationException("outer message", new ArgumentException("inner message"));

            GlobalExceptionHandling.WriteExceptionBlock("Unit test exception", ex, includeStackTrace: false);

            var output = writer.ToString();
            Assert.Contains("========== EXCEPTION ==========", output);
            Assert.Contains("Unit test exception", output);
            Assert.Contains("Type: System.InvalidOperationException", output);
            Assert.Contains("Message: outer message", output);
            Assert.Contains("--- Inner exception ---", output);
            Assert.Contains("Type: System.ArgumentException", output);
            Assert.Contains("Message: inner message", output);
            Assert.DoesNotContain("Stack trace:", output);
            Assert.DoesNotContain("Stack trace (inner):", output);
        }
        finally
        {
            Console.SetOut(previousOut);
        }
    }

    // F.I.R.S.T: deterministic, stack trace được tạo bằng throw/catch ngay trong test.
    // 3A — Arrange: tạo exception có stack trace. Act: WriteExceptionBlock includeStackTrace=true. Assert: output chứa nhãn Stack trace.
    [Fact]
    public void GEH02_WriteExceptionBlock_ShouldIncludeStackTrace_WhenEnabled()
    {
        var previousOut = Console.Out;
        var writer = new StringWriter(new StringBuilder());
        Console.SetOut(writer);

        try
        {
            Exception ex;
            try
            {
                ThrowInvalidOperation();
                throw new InvalidOperationException("This line should never execute.");
            }
            catch (Exception caught)
            {
                ex = caught;
            }

            GlobalExceptionHandling.WriteExceptionBlock("Stack trace test", ex, includeStackTrace: true);

            var output = writer.ToString();
            Assert.Contains("Stack trace:", output);
            Assert.Contains(nameof(ThrowInvalidOperation), output);
        }
        finally
        {
            Console.SetOut(previousOut);
        }
    }

    private static void ThrowInvalidOperation()
    {
        throw new InvalidOperationException("stack trace message");
    }
}
