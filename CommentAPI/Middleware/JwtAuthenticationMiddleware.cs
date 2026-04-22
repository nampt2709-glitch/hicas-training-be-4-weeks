using CommentAPI;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace CommentAPI.Middleware;

// Trừ đăng nhập, làm mới token và Swagger, các route /api còn lại bắt buộc đã xác thực JWT (access). Logout cần gửi access token.
public sealed class JwtAuthenticationMiddleware // Bảo vệ prefix /api ngoại trừ whitelist.
{
    private static readonly HashSet<string> AnonymousApiPaths = new(StringComparer.OrdinalIgnoreCase) // Đường dẫn không cần JWT.
    {
        "/api/auth/login", // Đăng nhập.
        "/api/auth/refresh" // Đổi refresh token.
    };

    private readonly RequestDelegate _next; // Bước pipeline kế tiếp.

    public JwtAuthenticationMiddleware(RequestDelegate next) // Middleware ctor.
    {
        _next = next; // Lưu delegate.
    }

    public async Task InvokeAsync(HttpContext context) // Entry mỗi request.
    {
        var path = NormalizePath(context.Request.Path); // Chuẩn hóa path (bỏ slash cuối).

        if (path == "/" || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)) // Root và Swagger luôn qua.
        {
            await _next(context); // Không chặn.
            return; // Xong.
        }

        if (AnonymousApiPaths.Contains(path)) // Whitelist auth endpoints.
        {
            await _next(context); // Cho qua ẩn danh.
            return; // Done.
        }

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
                return; // Stop.
            }
        }

        await _next(context); // Tiếp tục pipeline cho mọi route khác (không phải /api) hoặc /api đã auth.
    }

    private static string NormalizePath(PathString path) // Trim trailing slash.
    {
        var s = path.ToString().TrimEnd('/'); // Remove / cuối.
        return string.IsNullOrEmpty(s) ? "/" : s; // Root rỗng → "/".
    }
}

public static class JwtAuthenticationMiddlewareExtensions // Helper đăng ký middleware.
{
    public static IApplicationBuilder UseJwtAuthentication(this IApplicationBuilder app) => // Extension method.
        app.UseMiddleware<JwtAuthenticationMiddleware>(); // Thêm vào pipeline.
}
