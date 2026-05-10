using System.Security.Claims; // ClaimTypes.Role.
using System.Text.RegularExpressions; // Regex redact JSON.
using Microsoft.AspNetCore.Http; // StatusCodes.
using Serilog; // Log static + ForContext.
using Serilog.Events; // LogEventLevel, LogEvent.

namespace ApartmentAPI.Logging;

// Ghi log tách file qua Serilog: property LogChannel định tuyến tới ErrorsLog, AuditLog, SecurityLog, v.v.
public static class StructuredFileLogger
{ // Mở khối StructuredFileLogger.
    public const string LogChannelProperty = "LogChannel"; // Tên property filter sink theo kênh.
    public const string ErrorsChannel = "ERRORS"; // Kênh lỗi 4xx/5xx + stack.
    public const string AuditChannel = "AUDIT"; // Kênh audit: ai gọi endpoint nào, status.
    public const string SecurityChannel = "SECURITY"; // Kênh bảo mật: login fail, 401, v.v.
    public const string WarningsChannel = "WARNINGS"; // Cảnh báo chung.
    public const string FatalsChannel = "FATALS"; // Lỗi không hồi phục.
    public const string ActivitiesChannel = "ACTIVITIES"; // Request/response body (đã redact) nếu bật.

    // Body request đã đọc (đã redact) — GlobalExceptionHandler / 429 dùng cho ERRORS.
    public const string RequestBodyItemKey = "__ApartmentAPI_CapturedRequestBody";

    // Kiểm tra LogEvent có property LogChannel trùng channel (dùng trong sink filter).
    public static bool IsChannel(LogEvent e, string channel) =>
        e.Properties.TryGetValue(LogChannelProperty, out var v)
        && v is ScalarValue { Value: string s }
        && string.Equals(s, channel, StringComparison.Ordinal);

    // Định tuyến log ILogger từ các filter trong PipelineFilters.cs vào FiltersLog.log — khớp category CreateLogger(nameof(...)).
    public static bool IsPipelineFilterLog(LogEvent e)
    { // Mở khối IsPipelineFilterLog.
        // BƯỚC 1 — Đọc SourceContext scalar.
        if (!e.Properties.TryGetValue("SourceContext", out var v) || v is not ScalarValue sv)
            return false;
        var s = sv.Value?.ToString();
        if (string.IsNullOrEmpty(s))
            return false;
        // BƯỚC 2 — Suffix lớp filter Apartment* — nhất quán namespace filter.
        return s.EndsWith("ApartmentResourceFilter", StringComparison.Ordinal)
            || s.EndsWith("ApartmentActionFilter", StringComparison.Ordinal)
            || s.EndsWith("ApartmentExceptionFilter", StringComparison.Ordinal)
            || s.EndsWith("ApartmentResultFilter", StringComparison.Ordinal);
    } // Kết thúc IsPipelineFilterLog.

    // Che mật khẩu / token trong JSON thô (Activities / Errors).
    public static string RedactForLog(string? raw)
    { // Mở khối RedactForLog.
        // TRƯỜNG HỢP A — Không có body.
        if (string.IsNullOrEmpty(raw))
            return "";

        var s = raw;
        // BƯỚC 1 — Regex thay giá trị string sau các khóa nhạy cảm bằng "***".
        foreach (var key in new[] { "password", "currentPassword", "refreshToken", "accessToken", "refresh_token", "access_token" })
        {
            var pattern = $@"(?i)(""{Regex.Escape(key)}""\s*:\s*)""(?:[^""\\]|\\.)*""";
            s = Regex.Replace(s, pattern, "$1\"***\"");
        }

        // BƯỚC 2 — Cắt độ dài tối đa để log không phình.
        const int max = 8192;
        return s.Length <= max ? s : s[..max] + "...(truncated)";
    } // Kết thúc RedactForLog.

    // Ghi kênh ERRORS: status + code + type + message + body redacted + exception (optional).
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
        // BƯỚC 1 — Chọn mức log: 5xx Error, còn lại Warning.
        var level = statusCode >= StatusCodes.Status500InternalServerError ? LogEventLevel.Error : LogEventLevel.Warning;
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
        // BƯỚC 2 — Nếu có exception — ghi thêm dòng có stack (cùng level).
        if (exception is not null)
        {
            Log.ForContext(LogChannelProperty, ErrorsChannel)
                .Write(level, exception, "StackTrace (Errors) CorrelationId={CorrelationId}", correlationId);
        }
    } // Kết thúc Errors.

    // Audit: ai (user/roles) gọi method+path, status trả về.
    public static void Audit(
        string correlationId,
        string? userId,
        string? userName,
        string roles,
        string method,
        string path,
        int statusCode)
    { // Mở khối Audit.
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

    // Security Warning: sự kiện bảo mật có status tuỳ chọn.
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

    // Security Information: sự kiện an toàn muốn trace (vd đăng nhập thành công).
    public static void SecurityInfo(
        string correlationId,
        string eventType,
        string method,
        string path,
        string? userName,
        string? userId,
        string detail)
    { // Mở khối SecurityInfo.
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

    // Cảnh báo chung có thể kèm exception.
    public static void Warnings(string correlationId, string method, string path, string message, Exception? exception = null)
    { // Mở khối Warnings.
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

    // Fatal: shutdown / không thể tiếp tục host.
    public static void Fatals(string message, Exception? exception = null)
    { // Mở khối Fatals.
        Log.ForContext(LogChannelProperty, FatalsChannel)
            .Fatal(exception, "UTC={Utc:o} FATAL {Message}", DateTime.UtcNow, message);
    } // Kết thúc Fatals.

    // Activity: body request/response đã redact — chỉ bật khi cấu hình chi tiết.
    public static void Activity(
        string correlationId,
        string method,
        string path,
        int statusCode,
        string requestBody,
        string responseBody)
    { // Mở khối Activity.
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

    // Nối danh sách role từ ClaimsPrincipal — dùng Audit.
    public static string FormatRoles(ClaimsPrincipal user) =>
        string.Join(",",
            user.FindAll(ClaimTypes.Role)
                .Select(c => c.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase));
} // Kết thúc StructuredFileLogger.
