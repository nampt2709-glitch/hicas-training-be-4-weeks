using System.Diagnostics;
using System.Reflection;
using CommentAPI;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Middleware;

/// <summary>
/// Bắt mọi exception chưa xử lý trong pipeline (sau khi gỡ bọc Aggregate/TargetInvocation).
/// Body thành công và message trong <c>Ok(...)</c> là việc của controller — file này chỉ tạo JSON khi có exception.
/// </summary>
/// <remarks>
/// JWT challenge, ModelState, 403 authorization vẫn do Program / middleware khác (không throw vào đây).<br/>
/// Thứ tự nhánh: ApiException → Validation → SQL/EF → HTTP request sai → hủy/timeout → kiểu dữ liệu → InvalidOperation/Argument → còn lại 500.
/// </remarks>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        exception = Unwrap(exception);

        var correlationId = CorrelationMiddleware.GetCorrelationId(httpContext);
        if (!httpContext.Response.HasStarted)
        {
            httpContext.Response.Headers.Append(CorrelationMiddleware.HeaderName, correlationId);
        }

        httpContext.Response.ContentType = "application/json";
        CorrelationMiddleware.AppendErrorSourceHeader(
            httpContext,
            ResolveThrowingDescriptor(exception) ?? exception.GetType().FullName ?? "Exception");
        CorrelationMiddleware.TryAppendSqlQueryCountHeader(httpContext);

        if (exception is ApiException app)
        {
            LogForStatus(app.StatusCode, app, correlationId);
            httpContext.Response.StatusCode = app.StatusCode;
            await WriteErrorAsync(httpContext, app.ErrorCode, nameof(ApiException), app.ClientMessage, correlationId, app, cancellationToken);
            return true;
        }

        if (exception is ValidationException vex)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            var errors = vex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            await httpContext.Response.WriteAsJsonAsync(
                new
                {
                    code = ApiErrorCodes.ValidationFailed,
                    type = vex.GetType().Name,
                    message = ApiMessages.ValidationFailed,
                    correlationId,
                    errors,
                    detail = DevDetail(vex)
                },
                cancellationToken);
            return true;
        }

        if (exception is SqlException sqlDirect && TryMapSql(sqlDirect, out var st, out var cd, out var msg))
        {
            _logger.LogWarning(sqlDirect, "SqlException {CorrelationId}", correlationId);
            httpContext.Response.StatusCode = st;
            await WriteErrorAsync(httpContext, cd, nameof(SqlException), msg, correlationId, sqlDirect, cancellationToken);
            return true;
        }

        if (exception is DbUpdateException dbEx)
        {
            _logger.LogWarning(dbEx, "DbUpdateException {CorrelationId}", correlationId);
            if (FindSql(dbEx) is { } sqlInner && TryMapSql(sqlInner, out var st2, out var cd2, out var msg2))
            {
                httpContext.Response.StatusCode = st2;
                await WriteErrorAsync(httpContext, cd2, dbEx.GetType().Name, msg2, correlationId, dbEx, cancellationToken);
                return true;
            }

            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteErrorAsync(
                httpContext,
                ApiErrorCodes.DatabaseUpdateFailed,
                dbEx.GetType().Name,
                ApiMessages.InvalidRequest,
                correlationId,
                dbEx,
                cancellationToken);
            return true;
        }

        if (exception is BadHttpRequestException badReq)
        {
            _logger.LogWarning(badReq, "BadHttpRequest {CorrelationId}", correlationId);
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteErrorAsync(
                httpContext,
                ApiErrorCodes.InvalidOperation,
                badReq.GetType().Name,
                ApiMessages.InvalidRequest,
                correlationId,
                badReq,
                cancellationToken);
            return true;
        }

        if (exception is UnauthorizedAccessException denied)
        {
            _logger.LogWarning(denied, "UnauthorizedAccess {CorrelationId}", correlationId);
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await WriteErrorAsync(
                httpContext,
                ApiErrorCodes.Forbidden,
                denied.GetType().Name,
                ApiMessages.InsufficientPermission,
                correlationId,
                denied,
                cancellationToken);
            return true;
        }

        if (exception is OperationCanceledException cancelled)
        {
            _logger.LogWarning(cancelled, "Canceled {CorrelationId}", correlationId);
            httpContext.Response.StatusCode = StatusCodes.Status408RequestTimeout;
            await WriteErrorAsync(
                httpContext,
                ApiErrorCodes.RequestAborted,
                cancelled.GetType().Name,
                ApiMessages.RequestCancelled,
                correlationId,
                cancelled,
                cancellationToken);
            return true;
        }

        if (exception is FormatException or OverflowException or NotSupportedException)
        {
            _logger.LogWarning(exception, "BadInput {CorrelationId}", correlationId);
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteErrorAsync(
                httpContext,
                ApiErrorCodes.InvalidOperation,
                exception.GetType().Name,
                ApiMessages.InvalidRequest,
                correlationId,
                exception,
                cancellationToken);
            return true;
        }

        if (exception is InvalidOperationException io)
        {
            _logger.LogWarning(io, "InvalidOperation {CorrelationId}", correlationId);
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteErrorAsync(
                httpContext,
                ApiErrorCodes.InvalidOperation,
                io.GetType().Name,
                ApiMessages.InvalidRequest,
                correlationId,
                io,
                cancellationToken);
            return true;
        }

        if (exception is ArgumentException or ArgumentNullException)
        {
            _logger.LogWarning(exception, "Argument {CorrelationId}", correlationId);
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteErrorAsync(
                httpContext,
                ApiErrorCodes.InvalidOperation,
                exception.GetType().Name,
                ApiMessages.InvalidRequest,
                correlationId,
                exception,
                cancellationToken);
            return true;
        }

        _logger.LogError(exception, "Unhandled {CorrelationId}", correlationId);
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await WriteErrorAsync(
            httpContext,
            ApiErrorCodes.InternalError,
            exception.GetType().Name,
            ApiMessages.ServerError,
            correlationId,
            exception,
            cancellationToken);
        return true;
    }

    /// <summary>Gỡ AggregateException / TargetInvocation để nhánh is phía dưới khớp đúng loại thật.</summary>
    private static Exception Unwrap(Exception ex)
    {
        while (true)
        {
            switch (ex)
            {
                case AggregateException agg:
                {
                    var inner = agg.Flatten().InnerExceptions.FirstOrDefault();
                    if (inner is null)
                    {
                        return ex;
                    }

                    ex = inner;
                    continue;
                }
                case TargetInvocationException { InnerException: { } i }:
                    ex = i;
                    continue;
                default:
                    return ex;
            }
        }
    }

    private void LogForStatus(int status, Exception ex, string correlationId)
    {
        if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(ex, "ApiException {CorrelationId}", correlationId);
        }
        else
        {
            _logger.LogWarning(ex, "ApiException {CorrelationId}", correlationId);
        }
    }

    private async Task WriteErrorAsync(
        HttpContext http,
        string code,
        string type,
        string message,
        string correlationId,
        Exception ex,
        CancellationToken ct)
    {
        await http.Response.WriteAsJsonAsync(
            new
            {
                code,
                type,
                message,
                correlationId,
                detail = DevDetail(ex)
            },
            ct);
    }

    /// <summary>SQL Server: số lỗi → mã + câu trong ApiMessages.cs.</summary>
    private static bool TryMapSql(SqlException sql, out int httpStatus, out string code, out string message)
    {
        switch (sql.Number)
        {
            case 547:
                httpStatus = StatusCodes.Status400BadRequest;
                code = ApiErrorCodes.ForeignKeyViolation;
                message = ApiMessages.ForeignKeyViolation;
                return true;
            case 2627:
            case 2601:
                httpStatus = StatusCodes.Status409Conflict;
                code = ApiErrorCodes.DuplicateKey;
                message = ApiMessages.DuplicateKey;
                return true;
            default:
                httpStatus = default;
                code = default!;
                message = default!;
                return false;
        }
    }

    private static SqlException? FindSql(Exception ex)
    {
        for (var e = (Exception?)ex; e != null; e = e.InnerException)
        {
            if (e is SqlException s)
            {
                return s;
            }
        }

        return null;
    }

    private string? DevDetail(Exception exception)
    {
        if (!_environment.IsDevelopment())
        {
            return null;
        }

        var inner = exception.InnerException?.Message;
        return string.IsNullOrEmpty(inner) ? exception.Message : $"{exception.Message} | {inner}";
    }

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
