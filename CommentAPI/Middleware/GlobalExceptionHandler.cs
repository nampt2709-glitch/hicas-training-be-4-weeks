using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Middleware;

/// <summary>
/// Xử lý exception chưa bắt trong pipeline; body: code, type, message (và errors nếu có); nguồn lỗi: header X-CommentAPI-Error-Source; Correlation Id: X-Correlation-ID.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = CorrelationMiddleware.GetCorrelationId(httpContext);
        if (!httpContext.Response.HasStarted)
        {
            httpContext.Response.Headers.Append(CorrelationMiddleware.HeaderName, correlationId);
        }

        httpContext.Response.ContentType = "application/json";
        var errorSource = ResolveThrowingDescriptor(exception) ?? $"{exception.GetType().FullName} (no CommentAPI frame)";
        CorrelationMiddleware.AppendErrorSourceHeader(httpContext, errorSource);

        if (exception is ValidationException vex)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            var errors = vex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            await httpContext.Response
                .WriteAsJsonAsync(
                    new
                    {
                        code = ApiErrorCodes.ValidationFailed,
                        type = vex.GetType().Name,
                        message = ApiMessages.ValidationFailed,
                        errors
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        if (exception is DbUpdateException dbEx)
        {
            _logger.LogWarning(dbEx, "{ExceptionType} {CorrelationId}", dbEx.GetType().FullName, correlationId);
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response
                .WriteAsJsonAsync(
                    new
                    {
                        code = ApiErrorCodes.DatabaseUpdateFailed,
                        type = dbEx.GetType().Name,
                        message = ApiMessages.InvalidRequest
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        if (exception is InvalidOperationException ioEx)
        {
            _logger.LogWarning(ioEx, "{ExceptionType} {CorrelationId}", ioEx.GetType().FullName, correlationId);
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response
                .WriteAsJsonAsync(
                    new
                    {
                        code = ApiErrorCodes.InvalidOperation,
                        type = ioEx.GetType().Name,
                        message = ApiMessages.InvalidRequest
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        _logger.LogError(exception, "{ExceptionType} {CorrelationId}", exception.GetType().FullName, correlationId);
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response
            .WriteAsJsonAsync(
                new
                {
                    code = ApiErrorCodes.InternalError,
                    type = exception.GetType().Name,
                    message = ApiMessages.ServerError
                },
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Ước lượng vị trí ném exception trong mã ứng dụng (namespace CommentAPI), không đưa stack hay đường dẫn file.
    /// </summary>
    private static string? ResolveThrowingDescriptor(Exception exception)
    {
        foreach (var frame in new StackTrace(exception, fNeedFileInfo: false).GetFrames() ?? Array.Empty<StackFrame>())
        {
            var method = frame.GetMethod();
            var declaring = method?.DeclaringType;
            var ns = declaring?.Namespace;
            if (ns is null || !ns.StartsWith("CommentAPI", StringComparison.Ordinal))
            {
                continue;
            }

            var typeName = declaring!.FullName ?? declaring.Name;
            var methodName = method?.Name;
            return methodName is null ? typeName : $"{typeName}.{methodName}";
        }

        return null;
    }
}
