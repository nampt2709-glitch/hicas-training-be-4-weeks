using System.Security.Claims; // NameIdentifier, Name cho log SECURITY.
using ApartmentAPI; // ApiErrorCodes, ApiMessages.
using ApartmentAPI.Logging; // StructuredFileLogger.Security — file SecurityLog.
using Microsoft.AspNetCore.Authorization; // AuthorizationPolicy.
using Microsoft.AspNetCore.Authorization.Policy; // PolicyAuthorizationResult, handler mặc định.

namespace ApartmentAPI.Middleware;

// 403 JSON có correlation khi middleware authorization kết luận Forbidden (đã authenticate nhưng không đạt policy/role).
public sealed class ForbiddenHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new(); // Ủy quyền các trường hợp không phải Forbidden.

    public async Task HandleAsync(
        RequestDelegate next, // Middleware kế trong pipeline.
        HttpContext context, // Request hiện tại.
        AuthorizationPolicy policy, // Policy của endpoint đang được đánh giá.
        PolicyAuthorizationResult authorizeResult) // Forbidden / Challenged / Succeeded...
    {
        if (authorizeResult.Forbidden && !context.Response.HasStarted)
        {
            var correlationId = RequestPerformanceMiddleware.GetCorrelationId(context);
            context.Response.Headers.Append(RequestPerformanceMiddleware.HeaderName, correlationId);
            RequestPerformanceMiddleware.AppendErrorSourceHeader(context,
                $"{nameof(ForbiddenHandler)} (authorization policy forbids this request)");
            RequestPerformanceMiddleware.TryAppendSqlQueryCountHeader(context);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                code = ApiErrorCodes.Forbidden,
                type = "AuthorizationFailed",
                message = ApiMessages.InsufficientPermission,
                correlationId,
            }).ConfigureAwait(false);
            var uid = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var uname = context.User.Identity?.Name
                        ?? context.User.FindFirstValue(ClaimTypes.Name)
                        ?? context.User.FindFirstValue("unique_name")
                        ?? "";
            StructuredFileLogger.Security(
                correlationId,
                "ForbiddenInsufficientRole",
                context.Request.Method,
                context.Request.Path.Value ?? "",
                StatusCodes.Status403Forbidden,
                ApiMessages.InsufficientPermission,
                string.IsNullOrEmpty(uname) ? null : uname,
                string.IsNullOrEmpty(uid) ? null : uid);
            return;
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
    }
}
