using System.Security.Claims; // ClaimTypes.Role — định dạng chuỗi roles cho Audit.
using System.Text.RegularExpressions; // Regex.Replace — redact trường nhạy cảm trong JSON thô.
using Microsoft.AspNetCore.Http; // StatusCodes — chọn mức log Error vs Warning.
using Serilog; // Log static; ForContext gắn LogChannel.
using Serilog.Events; // LogEvent, LogEventLevel, ScalarValue.

namespace CommentAPI.Logging;

// =============================================================================
// File StructuredFileLogger.cs: helper ghi Serilog theo kênh (ERRORS, AUDIT, SECURITY, …) — Program định tuyến ra từng file .log.
// =============================================================================

// Ghi log tách file qua Serilog: mỗi dòng có timestamp (cấu hình sink). Dùng property LogChannel để định tuyến.
public static class StructuredFileLogger
{
    public const string LogChannelProperty = "LogChannel"; // Tên property Serilog để filter sink theo kênh.
    public const string ErrorsChannel = "ERRORS"; // File ErrorsLog.log — mọi response lỗi có cấu trúc.
    public const string AuditChannel = "AUDIT"; // File AuditLog.log — thao tác 2xx + user đã xác thực.
    public const string SecurityChannel = "SECURITY"; // File SecurityLog.log — challenge, auth, từ chối quyền.
    public const string WarningsChannel = "WARNINGS"; // File WarningsLog.log — sự kiện cảnh báo (SQL map, cancel, v.v.).
    public const string FatalsChannel = "FATALS"; // File FatalsLog.log — lỗi process/ Unhandled.
    public const string ActivitiesChannel = "ACTIVITIES"; // File ActivitiesLog.log — request/response đã redact.

    // Body request đã đọc (đã redact) — GlobalExceptionHandler đọc để ghi Errors.
    public const string RequestBodyItemKey = "__CommentAPI_CapturedRequestBody"; // Key Items cho snapshot POST/PUT.

    // Kiểm tra một LogEvent có property LogChannel đúng tên kênh hay không (dùng trong filter sink).
    public static bool IsChannel(LogEvent e, string channel) => // Predicate cho LoggerConfiguration.Filter.
        e.Properties.TryGetValue(LogChannelProperty, out var v) // Đọc property LogChannel.
        && v is ScalarValue { Value: string s } // Ép kiểu scalar string.
        && string.Equals(s, channel, StringComparison.Ordinal); // So khớp Ordinal.

    // Định tuyến log ILogger từ các filter trong PipelineFilters.cs vào FiltersLog.log — khớp category CreateLogger(nameof(...)).
    public static bool IsPipelineFilterLog(LogEvent e)
    { // Mở khối IsPipelineFilterLog.
        // BƯỚC 1 — Đọc SourceContext từ log event (Serilog enrich).
        if (!e.Properties.TryGetValue("SourceContext", out var v) || v is not ScalarValue sv)
            return false; // Không có context → không phải filter pipeline.

        // BƯỚC 2 — So hậu tố tên lớp filter đã đăng ký trong Program.Serilog.
        var s = sv.Value?.ToString();
        if (string.IsNullOrEmpty(s))
            return false;
        return s.EndsWith("CommentResourceFilter", StringComparison.Ordinal)
            || s.EndsWith("CommentActionFilter", StringComparison.Ordinal)
            || s.EndsWith("CommentExceptionFilter", StringComparison.Ordinal)
            || s.EndsWith("CommentResultFilter", StringComparison.Ordinal);
    } // Kết thúc IsPipelineFilterLog.

    // Che mật khẩu / token trong chuỗi JSON thô (log Activities / Errors).
    public static string RedactForLog(string? raw)
    { // Mở khối RedactForLog.
        // BƯỚC 1 — Chuỗi rỗng → trả rỗng (không log null literal).
        if (string.IsNullOrEmpty(raw))
        {
            return "";
        }

        // BƯỚC 2 — Thay từng khóa nhạy cảm bằng "***" qua regex (hỗ trợ escape trong JSON string).
        var s = raw;
        foreach (var key in new[] { "password", "currentPassword", "refreshToken", "accessToken", "refresh_token", "access_token" })
        {
            var pattern = $@"(?i)(""{Regex.Escape(key)}""\s*:\s*)""(?:[^""\\]|\\.)*""";
            s = Regex.Replace(s, pattern, "$1\"***\"");
        }

        // BƯỚC 3 — Cắt độ dài tối đa để file log không phình (8 KiB).
        const int max = 8192;
        return s.Length <= max ? s : s[..max] + "...(truncated)";
    } // Kết thúc RedactForLog.

    public static void Errors(
        string correlationId,
        int statusCode,
        string method,
        string path,
        string code,
        string type,
        string message,
        Exception? exception,
        string? requestBody)
    { // Mở khối Errors.
        // BƯỚC 1 — Chọn Warning cho 4xx, Error cho 5xx — cùng kênh ERRORS.
        var level = statusCode >= StatusCodes.Status500InternalServerError ? LogEventLevel.Error : LogEventLevel.Warning;

        // BƯỚC 2 — Ghi một dòng có cấu trúc (template + message đã redact body).
        Log.ForContext(LogChannelProperty, ErrorsChannel)
            .Write(level,
                "UTC={Utc:o} CorrelationId={CorrelationId} Status={StatusCode} {Method} {Path} Code={Code} Type={Type} Message={Message} RequestBody={RequestBody} ExceptionType={ExType} ExceptionMessage={ExMessage}",
                DateTime.UtcNow,
                correlationId,
                statusCode,
                method,
                path,
                code,
                type,
                message,
                requestBody ?? "",
                exception?.GetType().FullName ?? "",
                exception?.Message ?? "");

        // BƯỚC 3 — Nếu có exception — ghi thêm dòng có stack trace gắn CorrelationId.
        if (exception is not null)
        {
            Log.ForContext(LogChannelProperty, ErrorsChannel)
                .Write(level, exception, "StackTrace (Errors) CorrelationId={CorrelationId}", correlationId);
        }
    } // Kết thúc Errors.

    public static void Audit(
        string correlationId,
        string? userId,
        string? userName,
        string roles,
        string method,
        string path,
        int statusCode)
    { // Mở khối Audit.
        // BƯỚC 1 — Ghi Information vào kênh AUDIT: ai đã làm gì, kết quả HTTP bao nhiêu.
        Log.ForContext(LogChannelProperty, AuditChannel)
            .Information(
                "UTC={Utc:o} CorrelationId={CorrelationId} Status={StatusCode} UserId={UserId} UserName={UserName} Roles={Roles} {Method} {Path}",
                DateTime.UtcNow,
                correlationId,
                statusCode,
                userId ?? "",
                userName ?? "",
                roles,
                method,
                path);
    } // Kết thúc Audit.

    public static void Security(
        string correlationId,
        string eventType,
        string method,
        string path,
        int? statusCode,
        string detail,
        string? userName = null,
        string? userId = null)
    { // Mở khối Security.
        // BƯỚC 1 — Warning kênh SECURITY: sự kiện bảo mật (challenge, từ chối đăng nhập, v.v.).
        Log.ForContext(LogChannelProperty, SecurityChannel)
            .Warning(
                "UTC={Utc:o} CorrelationId={CorrelationId} Event={EventType} UserId={UserId} UserName={UserName} Status={Status} {Method} {Path} Detail={Detail}",
                DateTime.UtcNow,
                correlationId,
                eventType,
                userId ?? "",
                userName ?? "",
                statusCode,
                method,
                path,
                detail);
    } // Kết thúc Security.

    public static void SecurityInfo(
        string correlationId,
        string eventType,
        string method,
        string path,
        string? userName,
        string? userId,
        string detail)
    { // Mở khối SecurityInfo.
        // BƯỚC 1 — Information kênh SECURITY: luồng thành công cần trace (đăng nhập OK, refresh OK).
        Log.ForContext(LogChannelProperty, SecurityChannel)
            .Information(
                "UTC={Utc:o} CorrelationId={CorrelationId} Event={EventType} UserId={UserId} UserName={UserName} {Method} {Path} Detail={Detail}",
                DateTime.UtcNow,
                correlationId,
                eventType,
                userId ?? "",
                userName ?? "",
                method,
                path,
                detail);
    } // Kết thúc SecurityInfo.

    public static void Warnings(string correlationId, string method, string path, string message, Exception? exception = null)
    { // Mở khối Warnings.
        // BƯỚC 1 — Ghi Warning kênh WARNINGS — có thể kèm exception (SQL mapped, DbUpdate generic, v.v.).
        Log.ForContext(LogChannelProperty, WarningsChannel)
            .Warning(
                exception,
                "UTC={Utc:o} CorrelationId={CorrelationId} {Method} {Path} {Message}",
                DateTime.UtcNow,
                correlationId,
                method,
                path,
                message);
    } // Kết thúc Warnings.

    public static void Fatals(string message, Exception? exception = null)
    { // Mở khối Fatals.
        // BƯỚC 1 — Fatal kênh FATALS — AppDomain unhandled hoặc shutdown path.
        Log.ForContext(LogChannelProperty, FatalsChannel)
            .Fatal(exception, "UTC={Utc:o} FATAL {Message}", DateTime.UtcNow, message);
    } // Kết thúc Fatals.

    public static void Activity(
        string correlationId,
        string method,
        string path,
        int statusCode,
        string requestBody,
        string responseBody)
    { // Mở khối Activity.
        // BƯỚC 1 — Information kênh ACTIVITIES — payload request/response đã redact từ middleware.
        Log.ForContext(LogChannelProperty, ActivitiesChannel)
            .Information(
                "UTC={Utc:o} CorrelationId={CorrelationId} Status={StatusCode} {Method} {Path} RequestBody={RequestBody} ResponseBody={ResponseBody}",
                DateTime.UtcNow,
                correlationId,
                statusCode,
                method,
                path,
                requestBody,
                responseBody);
    } // Kết thúc Activity.

    // Nối danh sách role distinct — thứ tự không đảm bảo nhưng tránh trùng lặp không phân biệt hoa thường.
    public static string FormatRoles(ClaimsPrincipal user) => // Chuỗi một dòng cho cột Roles trong Audit.
        string.Join(",",
            user.FindAll(ClaimTypes.Role)
                .Select(c => c.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase));
} // Kết thúc lớp StructuredFileLogger.
