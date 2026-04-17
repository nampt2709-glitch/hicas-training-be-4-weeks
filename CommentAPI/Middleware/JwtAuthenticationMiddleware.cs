using CommentAPI;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace CommentAPI.Middleware;

/// <summary>
/// Trừ đăng nhập, làm mới token và Swagger, các route /api còn lại bắt buộc đã xác thực JWT (access). Logout cần gửi access token.
/// </summary>
public sealed class JwtAuthenticationMiddleware
{
    private static readonly HashSet<string> AnonymousApiPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/refresh"
    };

    private readonly RequestDelegate _next;

    public JwtAuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = NormalizePath(context.Request.Path);

        if (path == "/" || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (AnonymousApiPaths.Contains(path))
        {
            await _next(context);
            return;
        }

        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            // Nếu User chưa có principal sau UseAuthentication, ép AuthenticateAsync Bearer (một số cấu hình hosting).
            if (context.User.Identity is not { IsAuthenticated: true })
            {
                var auth = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme).ConfigureAwait(false);
                if (auth.Succeeded && auth.Principal?.Identity?.IsAuthenticated == true)
                {
                    context.User = auth.Principal;
                }
            }

            if (context.User.Identity is not { IsAuthenticated: true })
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                var cid = CorrelationMiddleware.GetCorrelationId(context);
                context.Response.Headers.Append(CorrelationMiddleware.HeaderName, cid);
                CorrelationMiddleware.AppendErrorSourceHeader(context,
                    $"{nameof(JwtAuthenticationMiddleware)} (API route requires authenticated user)");
                await context.Response.WriteAsJsonAsync(new
                {
                    code = ApiErrorCodes.Unauthenticated,
                    type = "AuthenticationFailed",
                    message = ApiMessages.Unauthenticated
                })
                    .ConfigureAwait(false);
                return;
            }
        }

        await _next(context);
    }

    private static string NormalizePath(PathString path)
    {
        var s = path.ToString().TrimEnd('/');
        return string.IsNullOrEmpty(s) ? "/" : s;
    }
}

public static class JwtAuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseJwtAuthentication(this IApplicationBuilder app) =>
        app.UseMiddleware<JwtAuthenticationMiddleware>();
}
