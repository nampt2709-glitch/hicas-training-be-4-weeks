using CommentAPI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace CommentAPI.Middleware;

/// <summary>
/// Đã xác thực nhưng thiếu quyền: trả 403 JSON thay vì body trống.
/// </summary>
public sealed class ForbiddenHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && !context.Response.HasStarted)
        {
            var correlationId = CorrelationMiddleware.GetCorrelationId(context);
            context.Response.Headers.Append(CorrelationMiddleware.HeaderName, correlationId);
            CorrelationMiddleware.AppendErrorSourceHeader(context,
                $"{nameof(ForbiddenHandler)} (authorization policy forbids this request)");
            CorrelationMiddleware.TryAppendSqlQueryCountHeader(context);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response
                .WriteAsJsonAsync(new
                {
                    code = ApiErrorCodes.Forbidden,
                    type = "AuthorizationFailed",
                    message = ApiMessages.InsufficientPermission
                })
                .ConfigureAwait(false);
            return;
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
    }
}
