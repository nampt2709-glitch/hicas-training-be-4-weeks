using System.Security.Claims; // ClaimTypes.NameIdentifier — đọc user id cho Audit.
using CommentAPI.Logging; // StructuredFileLogger.Activity, Audit, RedactForLog.
using Microsoft.AspNetCore.Http; // HttpContext, RequestDelegate, UseMiddleware.

namespace CommentAPI.Middleware;

// =============================================================================
// File ActivityAndAuditLoggingMiddleware.cs: bọc Response.Body trong MemoryStream để đọc body ra log ACTIVITY;
// AUDIT khi 2xx + user đã xác thực. Đặt NGOÀI UseExceptionHandler để body lỗi vẫn đi qua buffer.
// =============================================================================

// Ghi ACTIVITY (request/response + status + correlation) và AUDIT (2xx + user đã xác thực). Đặt NGOÀI UseExceptionHandler để body lỗi vẫn đi qua memory stream.
public sealed class ActivityAndAuditLoggingMiddleware
{
    private const long MaxRequestBytes = 98_304; // ~96 KiB — giới hạn đọc body log (tránh OutOfMemory).
    private readonly RequestDelegate _next; // Bước kế tiếp trong pipeline.

    public ActivityAndAuditLoggingMiddleware(RequestDelegate next) => _next = next; // Tiêm delegate pipeline.

    public async Task InvokeAsync(HttpContext context)
    { // Mở khối InvokeAsync.
        var path = context.Request.Path.Value ?? ""; // Đường dẫn relative.

        // BƯỚC 1 — Swagger không log Activity (nhiễu, payload lớn từ UI).
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // BƯỚC 2 — Cho phép đọc lại body nhiều lần (model binding + log).
        context.Request.EnableBuffering();
        var requestSnapshot = ""; // Snapshot đã redact hoặc placeholder.

        // BƯỚC 3 — multipart không log nội dung; POST/PUT/PATCH có ContentLength nhỏ — đọc text + RedactForLog.
        if (context.Request.HasFormContentType)
        {
            requestSnapshot = "(form-data; request body not logged)";
        }
        else if (context.Request.ContentLength is > 0 and <= MaxRequestBytes
                 && (HttpMethods.IsPost(context.Request.Method)
                     || HttpMethods.IsPut(context.Request.Method)
                     || HttpMethods.IsPatch(context.Request.Method)))
        {
            context.Request.Body.Position = 0;
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var raw = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            requestSnapshot = StructuredFileLogger.RedactForLog(raw);
        }

        context.Items[StructuredFileLogger.RequestBodyItemKey] = requestSnapshot; // ERRORS/GlobalException có thể đọc.

        // BƯỚC 4 — Thay Response.Body bằng MemoryStream để capture response trước khi gửi client.
        var originalBody = context.Response.Body;
        await using var mem = new MemoryStream();
        context.Response.Body = mem;

        try
        {
            await _next(context); // Chạy phần còn lại của pipeline (controller, filter, v.v.).
        }
        finally
        { // BƯỚC 5 — Đọc response đã capture, redact, copy về stream gốc; ghi Activity + có thể Audit.
            mem.Position = 0;
            var respText = await new StreamReader(mem).ReadToEndAsync();
            mem.Position = 0;
            await mem.CopyToAsync(originalBody);
            context.Response.Body = originalBody;

            var cid = RequestPerformanceMiddleware.GetCorrelationId(context);
            var status = context.Response.StatusCode;
            var method = context.Request.Method;
            var redactedResp = StructuredFileLogger.RedactForLog(respText);

            StructuredFileLogger.Activity(cid, method, path, status, requestSnapshot, redactedResp);

            if (status is >= 200 and < 300 && context.User.Identity is { IsAuthenticated: true })
            {
                var uid = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? context.User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);
                var uname = context.User.Identity.Name
                            ?? context.User.FindFirstValue(ClaimTypes.Name)
                            ?? context.User.FindFirstValue("unique_name")
                            ?? "?";
                var roles = StructuredFileLogger.FormatRoles(context.User);
                StructuredFileLogger.Audit(cid, uid, uname, roles, method, path, status);
            }
        }
    } // Kết thúc InvokeAsync.
} // Kết thúc lớp ActivityAndAuditLoggingMiddleware.

public static class ActivityAndAuditLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseActivityAndAuditLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<ActivityAndAuditLoggingMiddleware>();
} // Kết thúc lớp ActivityAndAuditLoggingMiddlewareExtensions.
