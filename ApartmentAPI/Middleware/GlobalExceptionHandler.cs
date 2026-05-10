using System.Diagnostics; // StackTrace để ghép điểm ném ApartmentAPI trong header lỗi.
using System.Reflection; // TargetInvocationException khi unwrap.
using ApartmentAPI.Logging; // StructuredFileLogger ghi Errors/Security/Warnings.
using FluentValidation; // ValidationException FluentValidation → 400.
using Microsoft.AspNetCore.Diagnostics; // IExceptionHandler, TryHandleAsync.
using Microsoft.AspNetCore.Http; // HttpContext, StatusCodes, WriteAsJsonAsync.
using Microsoft.Data.SqlClient; // SqlException (số lỗi 547 / 2627 / 2601).
using Microsoft.EntityFrameworkCore; // DbUpdateException bọc FK/unique.

namespace ApartmentAPI.Middleware;

// Bắt ngoại lệ cuối pipeline: trả JSON thống nhất + log file ERRORS/SECURITY/WARNINGS (đồng bộ tinh thần CommentAPI).
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    #region Dependencies

    private readonly ILogger<GlobalExceptionHandler> _logger; // Log ILogger tiêu chuẩn (mức Error/Warning theo ApiException.StatusCode).

    private readonly IHostEnvironment _environment; // Chi tiết lỗi (detail) chỉ khi Development.

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment)
    {
        _logger = logger; // Tiêm logger.
        _environment = environment; // Tiêm môi trường host.
    }

    #endregion

    // Lấy snapshot body đã buffer trong middleware (chuỗi rỗng nếu không có).
    private static string RequestBodySnapshot(HttpContext http) =>
        http.Items.TryGetValue(StructuredFileLogger.RequestBodyItemKey, out var v) ? v?.ToString() ?? "" : "";

    // Ghi một sự kiện ERRORS ra file (StructuredFileLogger) kèm body snapshot.
    private static void FileLogError(
        HttpContext http,
        string correlationId,
        int statusCode,
        string code,
        string type,
        string message,
        Exception exception) =>
        StructuredFileLogger.Errors(
            correlationId,
            statusCode,
            http.Request.Method,
            http.Request.Path.Value ?? "",
            code,
            type,
            message,
            exception,
            RequestBodySnapshot(http));

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // BƯỚC 1 — Bóc AggregateException/TargetInvocation để khớp loại exception thực.
        exception = Unwrap(exception);
        // BƯỚC 2 — Correlation id thống nhất với middleware hiệu năng / log.
        var correlationId = RequestPerformanceMiddleware.GetCorrelationId(httpContext);
        if (!httpContext.Response.HasStarted)
            httpContext.Response.Headers.Append(RequestPerformanceMiddleware.HeaderName, correlationId);
        httpContext.Response.ContentType = "application/json";
        RequestPerformanceMiddleware.AppendErrorSourceHeader(
            httpContext,
            ResolveThrowingDescriptor(exception) ?? exception.GetType().FullName ?? "Exception");
        RequestPerformanceMiddleware.TryAppendSqlQueryCountHeader(httpContext);
        // TRƯỜNG HỢP ApiException — mã/status đã định nghĩa trong service (bao gồm auth lỗi).
        if (exception is ApiException app)
        {
            LogForStatus(app.StatusCode, app, correlationId);
            httpContext.Response.StatusCode = app.StatusCode;
            await WriteErrorAsync(
                httpContext,
                app.ErrorCode,
                nameof(ApiException),
                app.ClientMessage,
                correlationId,
                app,
                cancellationToken);
            FileLogError(httpContext, correlationId, app.StatusCode, app.ErrorCode, nameof(ApiException), app.ClientMessage, app);
            if (app.ErrorCode is ApiErrorCodes.LoginFailed or ApiErrorCodes.RefreshFailed)
            {
                StructuredFileLogger.Security(
                    correlationId,
                    "AuthCredentialRejected",
                    httpContext.Request.Method,
                    httpContext.Request.Path.Value ?? "",
                    app.StatusCode,
                    $"{app.ErrorCode}: {app.ClientMessage}");
            }
            return true;
        }
        // TRƯỜNG HỢP FluentValidation — gom Errors theo tên thuộc tính.
        if (exception is ValidationException vex)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            var errors = vex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            var errSummary = string.Join("; ", vex.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
            await httpContext.Response.WriteAsJsonAsync(
                new
                {
                    code = ApiErrorCodes.Validation,
                    type = vex.GetType().Name,
                    message = ApiMessages.ValidationFailed,
                    correlationId,
                    errors,
                    detail = DevDetail(vex),
                },
                cancellationToken);
            FileLogError(httpContext, correlationId, StatusCodes.Status400BadRequest, ApiErrorCodes.Validation, vex.GetType().Name, errSummary, vex);
            return true;
        }
        // TRƯỜNG HỢP SqlException đã map (FK / duplicate key).
        if (exception is SqlException sqlDirect && TryMapSql(sqlDirect, out var st, out var cd, out var msg))
        {
            _logger.LogWarning(sqlDirect, "SqlException {CorrelationId}", correlationId);
            httpContext.Response.StatusCode = st;
            await WriteErrorAsync(httpContext, cd, nameof(SqlException), msg, correlationId, sqlDirect, cancellationToken);
            FileLogError(httpContext, correlationId, st, cd, nameof(SqlException), msg, sqlDirect);
            StructuredFileLogger.Warnings(correlationId, httpContext.Request.Method, httpContext.Request.Path.Value ?? "", $"SqlException mapped: {cd}", sqlDirect);
            return true;
        }
        // TRƯỜNG HỢP SqlException không map được — 500 an toàn.
        if (exception is SqlException sqlUnmapped)
        {
            _logger.LogError(sqlUnmapped, "SqlException unmapped {CorrelationId}", correlationId);
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await WriteErrorAsync(
                httpContext,
                ApiErrorCodes.InternalError,
                nameof(SqlException),
                ApiMessages.ServerError,
                correlationId,
                sqlUnmapped,
                cancellationToken);
            FileLogError(
                httpContext,
                correlationId,
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalError,
                nameof(SqlException),
                ApiMessages.ServerError,
                sqlUnmapped);
            return true;
        }
        // TRƯỜNG HỢP EF SaveChanges bọc SqlException hoặc lỗi cập nhật chung.
        if (exception is DbUpdateException dbEx)
        {
            _logger.LogWarning(dbEx, "DbUpdateException {CorrelationId}", correlationId);
            if (FindSql(dbEx) is { } sqlInner && TryMapSql(sqlInner, out var st2, out var cd2, out var msg2))
            {
                httpContext.Response.StatusCode = st2;
                await WriteErrorAsync(httpContext, cd2, dbEx.GetType().Name, msg2, correlationId, dbEx, cancellationToken);
                FileLogError(httpContext, correlationId, st2, cd2, dbEx.GetType().Name, msg2, dbEx);
                StructuredFileLogger.Warnings(correlationId, httpContext.Request.Method, httpContext.Request.Path.Value ?? "", "DbUpdateException (inner SQL mapped)", dbEx);
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
            FileLogError(
                httpContext,
                correlationId,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.DatabaseUpdateFailed,
                dbEx.GetType().Name,
                ApiMessages.InvalidRequest,
                dbEx);
            StructuredFileLogger.Warnings(correlationId, httpContext.Request.Method, httpContext.Request.Path.Value ?? "", "DbUpdateException (generic)", dbEx);
            return true;
        }
        // TRƯỜNG HỢP request HTTP không hợp lệ (Kestrel).
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
            FileLogError(
                httpContext,
                correlationId,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.InvalidOperation,
                badReq.GetType().Name,
                ApiMessages.InvalidRequest,
                badReq);
            StructuredFileLogger.Warnings(correlationId, httpContext.Request.Method, httpContext.Request.Path.Value ?? "", "BadHttpRequestException", badReq);
            return true;
        }
        // TRƯỜNG HỢP không đủ quyền ở tầng code (policy xử khác là ForbiddenHandler).
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
            FileLogError(
                httpContext,
                correlationId,
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.Forbidden,
                denied.GetType().Name,
                ApiMessages.InsufficientPermission,
                denied);
            StructuredFileLogger.Security(
                correlationId,
                "UnauthorizedAccessException",
                httpContext.Request.Method,
                httpContext.Request.Path.Value ?? "",
                StatusCodes.Status403Forbidden,
                denied.Message);
            return true;
        }
        // TRƯỜNG HỢP hủy / timeout client.
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
            FileLogError(
                httpContext,
                correlationId,
                StatusCodes.Status408RequestTimeout,
                ApiErrorCodes.RequestAborted,
                cancelled.GetType().Name,
                ApiMessages.RequestCancelled,
                cancelled);
            StructuredFileLogger.Warnings(correlationId, httpContext.Request.Method, httpContext.Request.Path.Value ?? "", "Request cancelled / timeout", cancelled);
            return true;
        }
        // TRƯỜNG HỢP định dạng / tràn số / thao tác không hỗ trợ.
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
            FileLogError(
                httpContext,
                correlationId,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.InvalidOperation,
                exception.GetType().Name,
                ApiMessages.InvalidRequest,
                exception);
            return true;
        }
        // TRƯỜNG HỢP InvalidOperationException — thường là business guard.
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
            FileLogError(
                httpContext,
                correlationId,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.InvalidOperation,
                io.GetType().Name,
                ApiMessages.InvalidRequest,
                io);
            return true;
        }
        // TRƯỜNG HỢP tham số không hợp lệ.
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
            FileLogError(
                httpContext,
                correlationId,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.InvalidOperation,
                exception.GetType().Name,
                ApiMessages.InvalidRequest,
                exception);
            return true;
        }
        // TRƯỜNG HỢP mặc định — ngoại lệ chưa phân loại → 500.
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
        FileLogError(
            httpContext,
            correlationId,
            StatusCodes.Status500InternalServerError,
            ApiErrorCodes.InternalError,
            exception.GetType().Name,
            ApiMessages.ServerError,
            exception);
        return true;
    }

    // Bóc lồng AggregateException / TargetInvocationException để lấy gốc.
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
                        return ex;
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

    // ApiException: 5xx log Error, còn lại Warning (tránh ồn 401/404).
    private void LogForStatus(int status, Exception ex, string correlationId)
    {
        if (status >= StatusCodes.Status500InternalServerError)
            _logger.LogError(ex, "ApiException {CorrelationId}", correlationId);
        else
            _logger.LogWarning(ex, "ApiException {CorrelationId}", correlationId);
    }

    // Payload JSON thống nhất: code, type, message, correlationId, detail (dev).
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
                detail = DevDetail(ex),
            },
            ct);
    }

    // Ánh xạ số lỗi SQL Server sang HTTP + mã lỗi API (547 FK, 2627/2601 unique).
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

    // Duyệt InnerException để tìm SqlException (EF bọc nhiều lớp).
    private static SqlException? FindSql(Exception ex)
    {
        for (var e = (Exception?)ex; e != null; e = e.InnerException)
        {
            if (e is SqlException s)
                return s;
        }
        return null;
    }

    // Chi tiết kỹ thuật chỉ trả về client khi Development.
    private string? DevDetail(Exception exception)
    {
        if (!_environment.IsDevelopment())
            return null;
        var inner = exception.InnerException?.Message;
        return string.IsNullOrEmpty(inner) ? exception.Message : $"{exception.Message} | {inner}";
    }

    // Khung stack đầu tiên thuộc namespace ApartmentAPI → ghi vào header Error-Source.
    private static string? ResolveThrowingDescriptor(Exception exception)
    {
        foreach (var frame in new StackTrace(exception, fNeedFileInfo: false).GetFrames() ?? Array.Empty<StackFrame>())
        {
            var method = frame.GetMethod();
            var declaring = method?.DeclaringType;
            var ns = declaring?.Namespace;
            if (ns is null || !ns.StartsWith("ApartmentAPI", StringComparison.Ordinal))
                continue;
            var typeName = declaring!.FullName ?? declaring.Name;
            var methodName = method?.Name;
            return methodName is null ? typeName : $"{typeName}.{methodName}";
        }
        return null;
    }
}
