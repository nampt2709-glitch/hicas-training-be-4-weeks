using ApartmentAPI; // ApiMessages, ApiErrorCodes.
using ApartmentAPI.Logging; // SECURITY log khi thiếu user trên route /api.
using Microsoft.AspNetCore.Authentication; // AuthenticateAsync.
using Microsoft.AspNetCore.Authentication.JwtBearer; // Scheme mặc định Bearer.

namespace ApartmentAPI.Middleware;

// Sau UseAuthentication — thử hydrate User từ Bearer; với prefix /api (trừ whitelist) trả 401 JSON nếu vẫn chưa authenticated.
public sealed class JwtAuthenticationMiddleware
{
    private static readonly HashSet<string> AnonymousApiPaths = new(StringComparer.OrdinalIgnoreCase) // Không ép JWT (auth công khai).
    {
        "/api/auth/signup",
        "/api/auth/login",
        "/api/auth/refresh"
    };

    // Cho phép signup/login/refresh trên mọi segment phiên bản: /api/v1.0/auth/... và /api/v2.0/auth/... (ApiVersioning).
    private static bool IsVersionedAuthAnonymousPath(string normalizedPath)
    {
        if (!normalizedPath.StartsWith("/api/v", StringComparison.OrdinalIgnoreCase))
            return false;
        return normalizedPath.EndsWith("/auth/signup", StringComparison.OrdinalIgnoreCase)
               || normalizedPath.EndsWith("/auth/login", StringComparison.OrdinalIgnoreCase)
               || normalizedPath.EndsWith("/auth/refresh", StringComparison.OrdinalIgnoreCase);
    }

    private readonly RequestDelegate _next; // Delegate pipeline kế tiếp.

    public JwtAuthenticationMiddleware(RequestDelegate next) => _next = next; // Tiêm bởi UseMiddleware.

    public async Task InvokeAsync(HttpContext context) // middleware per-request.
    {
        var path = NormalizePath(context.Request.Path);

        if (path == "/" || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (AnonymousApiPaths.Contains(path) || IsVersionedAuthAnonymousPath(path))
        {
            await _next(context);
            return;
        }

        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            if (context.User.Identity is not { IsAuthenticated: true })
            {
                var auth = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme).ConfigureAwait(false);
                if (auth.Succeeded && auth.Principal?.Identity?.IsAuthenticated == true)
                    context.User = auth.Principal;
            }

            if (context.User.Identity is not { IsAuthenticated: true })
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                var cid = RequestPerformanceMiddleware.GetCorrelationId(context);
                context.Response.Headers.Append(RequestPerformanceMiddleware.HeaderName, cid);
                RequestPerformanceMiddleware.AppendErrorSourceHeader(context,
                    $"{nameof(JwtAuthenticationMiddleware)} (API route requires authenticated user)");
                await context.Response.WriteAsJsonAsync(new
                {
                    code = ApiErrorCodes.Unauthenticated,
                    type = "AuthenticationFailed",
                    message = ApiMessages.Unauthenticated
                }).ConfigureAwait(false);
                StructuredFileLogger.Security(
                    cid,
                    "ApiRequiresAuthentication",
                    context.Request.Method,
                    path,
                    StatusCodes.Status401Unauthorized,
                    "JWT middleware: authenticated user required");
                return;
            }
        }

        await _next(context);
    }

    private static string NormalizePath(PathString path) // So sánh whitelist không phụ thuộc trailing slash.
    {
        var s = path.ToString().TrimEnd('/');
        return string.IsNullOrEmpty(s) ? "/" : s;
    }
}

// Extension đăng ký middleware theo convention UseXxx trong Program.cs.
public static class JwtAuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseJwtAuthentication(this IApplicationBuilder app) =>
        app.UseMiddleware<JwtAuthenticationMiddleware>();
}
