using System.Text.RegularExpressions; // Regex whitelist auth ẩn danh theo version URL.
using CommentAPI; // ApiErrorCodes, ApiMessages — body JSON 401 thống nhất.
using CommentAPI.Logging; // StructuredFileLogger.Security.
using Microsoft.AspNetCore.Authentication; // AuthenticateAsync(JwtBearerDefaults…).
using Microsoft.AspNetCore.Authentication.JwtBearer; // Scheme Bearer sau UseAuthentication.
using Microsoft.AspNetCore.Http; // HttpContext, StatusCodes, WriteAsJsonAsync.

namespace CommentAPI.Middleware;

// =============================================================================
// File JwtAuthenticationMiddleware.cs: sau UseAuthentication — mọi /api (trừ auth signup/login/refresh và Swagger) bắt buộc có principal authenticated.
// =============================================================================

// Trừ đăng nhập (segment version), làm mới token và Swagger, các route /api còn lại bắt buộc đã xác thực JWT (access). Logout cần gửi access token.
public sealed class JwtAuthenticationMiddleware // Bảo vệ prefix /api ngoại trừ whitelist.
{
    // Đường dẫn ẩn danh: /api/v1/auth/signup, /api/v2.0/auth/login, … (khớp mọi segment version hợp lệ trong URL).
    private static readonly Regex AnonymousAuthPath = new(
        @"^/api/v[\d.]+/auth/(signup|login|refresh)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly RequestDelegate _next; // Bước pipeline kế tiếp.

    public JwtAuthenticationMiddleware(RequestDelegate next) // Middleware ctor.
    {
        _next = next; // Lưu delegate.
    }

    public async Task InvokeAsync(HttpContext context) // Entry mỗi request.
    { // Mở khối InvokeAsync.
        // BƯỚC 1 — Chuẩn hóa path (bỏ slash cuối) để regex whitelist khớp ổn định.
        var path = NormalizePath(context.Request.Path); // Chuẩn hóa path (bỏ slash cuối).

        // BƯỚC 2 — Root và Swagger không chặn (UI thử API, redirect gốc).
        if (path == "/" || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)) // Root và Swagger luôn qua.
        {
            await _next(context); // Không chặn.
            return; // Xong.
        }

        // BƯỚC 3 — Các endpoint auth công khai theo version cho qua không cần Bearer.
        if (AnonymousAuthPath.IsMatch(path)) // Whitelist auth endpoints theo version (v1, v2, …).
        {
            await _next(context); // Cho qua ẩn danh.
            return; // Done.
        }

        // BƯỚC 4 — Chỉ với prefix /api: đảm bảo đã Authenticate Bearer (thử lại lần nữa nếu principal trống).
        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)) // Chỉ áp quy tắc cho API.
        {
            // Nếu User chưa có principal sau UseAuthentication, ép AuthenticateAsync Bearer (một số cấu hình hosting).
            if (context.User.Identity is not { IsAuthenticated: true }) // Principal chưa authenticated.
            {
                var auth = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme).ConfigureAwait(false); // Thử parse Bearer.
                if (auth.Succeeded && auth.Principal?.Identity?.IsAuthenticated == true) // Có principal hợp lệ.
                {
                    context.User = auth.Principal; // Gán vào HttpContext.User.
                }
            }

            // BƯỚC 5 — Nếu vẫn chưa authenticated — trả 401 JSON + SECURITY log rồi dừng pipeline.
            if (context.User.Identity is not { IsAuthenticated: true }) // Vẫn chưa đăng nhập.
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized; // 401.
                context.Response.ContentType = "application/json"; // JSON.
                var cid = RequestPerformanceMiddleware.GetCorrelationId(context); // Correlation.
                context.Response.Headers.Append(RequestPerformanceMiddleware.HeaderName, cid); // Header.
                RequestPerformanceMiddleware.AppendErrorSourceHeader(context, // Error source.
                    $"{nameof(JwtAuthenticationMiddleware)} (API route requires authenticated user)"); // Text.
                await context.Response.WriteAsJsonAsync(new // Body.
                {
                    code = ApiErrorCodes.Unauthenticated, // Code.
                    type = "AuthenticationFailed", // Type.
                    message = ApiMessages.Unauthenticated // Message.
                })
                    .ConfigureAwait(false); // Async.
                StructuredFileLogger.Security( // Nhóm SECURITY: /api không Bearer nhưng không nằm whitelist ẩn danh.
                    cid,
                    "ApiRequiresAuthentication",
                    context.Request.Method,
                    path,
                    StatusCodes.Status401Unauthorized,
                    "JWT middleware: authenticated user required");
                return; // Stop.
            }
        }

        // BƯỚC 6 — Route không phải /api hoặc /api đã có user — tiếp tục pipeline.
        await _next(context); // Tiếp tục pipeline cho mọi route khác (không phải /api) hoặc /api đã auth.
    } // Kết thúc InvokeAsync.

    private static string NormalizePath(PathString path) // Trim trailing slash.
    {
        var s = path.ToString().TrimEnd('/'); // Remove / cuối.
        return string.IsNullOrEmpty(s) ? "/" : s; // Root rỗng → "/".
    } // Kết thúc NormalizePath.
} // Kết thúc lớp JwtAuthenticationMiddleware.

public static class JwtAuthenticationMiddlewareExtensions // Helper đăng ký middleware.
{
    public static IApplicationBuilder UseJwtAuthentication(this IApplicationBuilder app) => // Extension method.
        app.UseMiddleware<JwtAuthenticationMiddleware>(); // Thêm vào pipeline.
} // Kết thúc lớp JwtAuthenticationMiddlewareExtensions.
