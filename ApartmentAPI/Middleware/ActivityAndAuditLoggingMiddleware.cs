using System.Security.Claims; // NameIdentifier để audit uid.
using ApartmentAPI.Logging; // Activity / Audit channels + RedactForLog.
using Microsoft.AspNetCore.Http; // EnableBuffering, response buffer.

namespace ApartmentAPI.Middleware;

// Ghi nhật ký hoạt động (toàn payload redact) và audit thành công kèm user/role — buffer response để đọc body trả về.
public sealed class ActivityAndAuditLoggingMiddleware
{
    private const long MaxRequestBytes = 98_304;
    private readonly RequestDelegate _next;

    public ActivityAndAuditLoggingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        context.Request.EnableBuffering();
        var requestSnapshot = "";
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

        context.Items[StructuredFileLogger.RequestBodyItemKey] = requestSnapshot;

        var originalBody = context.Response.Body;
        await using var mem = new MemoryStream();
        context.Response.Body = mem;

        try
        {
            await _next(context);
        }
        finally
        {
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
    }
}

public static class ActivityAndAuditLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseActivityAndAuditLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<ActivityAndAuditLoggingMiddleware>();
}
