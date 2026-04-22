using System.Diagnostics; 
using System.Reflection; 
using CommentAPI; 
using FluentValidation; 
using Microsoft.AspNetCore.Diagnostics; 
using Microsoft.AspNetCore.Http; 
using Microsoft.Data.SqlClient; 
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Middleware;

// Bắt mọi exception chưa xử lý trong pipeline (sau khi gỡ bọc Aggregate/TargetInvocation).
// Body thành công và message trong Ok(...) là việc của controller — file này chỉ tạo JSON khi có exception.
// JWT challenge, ModelState, 403 authorization vẫn do Program / middleware khác (không throw vào đây).
// Thứ tự nhánh: ApiException → Validation → SQL/EF → HTTP request sai → hủy/timeout → kiểu dữ liệu → InvalidOperation/Argument → còn lại 500.
public sealed class GlobalExceptionHandler : IExceptionHandler // ASP.NET Core global handler.
{
    private readonly ILogger<GlobalExceptionHandler> _logger; // Log theo mức độ nghiêm trọng.
    private readonly IHostEnvironment _environment; // Development → chi tiết lỗi trong JSON.

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment) // DI.
    {
        _logger = logger; // Logger.
        _environment = environment; // Env.
    }

    public async ValueTask<bool> TryHandleAsync( // Trả true nếu đã xử lý (luôn true ở đây).
        HttpContext httpContext, // Context hiện tại.
        Exception exception, // Exception gốc hoặc đã unwrap.
        CancellationToken cancellationToken) // Hủy ghi response.
    {
        exception = Unwrap(exception); // Gỡ lớp bọc phổ biến.

        var correlationId = RequestPerformanceMiddleware.GetCorrelationId(httpContext); // Correlation id.
        if (!httpContext.Response.HasStarted) // Chỉ set header nếu chưa flush.
        {
            httpContext.Response.Headers.Append(RequestPerformanceMiddleware.HeaderName, correlationId); // Echo correlation.
        }

        httpContext.Response.ContentType = "application/json"; // Luôn JSON lỗi.
        RequestPerformanceMiddleware.AppendErrorSourceHeader( // Header vận hành.
            httpContext, // Context.
            ResolveThrowingDescriptor(exception) ?? exception.GetType().FullName ?? "Exception"); // Descriptor.
        RequestPerformanceMiddleware.TryAppendSqlQueryCountHeader(httpContext); // Đếm SQL nếu có.

        if (exception is ApiException app) // Lỗi nghiệp vụ có mã HTTP cố định.
        {
            LogForStatus(app.StatusCode, app, correlationId); // Warning vs Error.
            httpContext.Response.StatusCode = app.StatusCode; // Status từ exception.
            await WriteErrorAsync(httpContext, app.ErrorCode, nameof(ApiException), app.ClientMessage, correlationId, app, cancellationToken); // JSON.
            return true; // Handled.
        }

        if (exception is ValidationException vex) // FluentValidation tổng hợp lỗi field.
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest; // 400.
            var errors = vex.Errors // Danh sách lỗi.
                .GroupBy(e => e.PropertyName) // Theo property.
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()); // Mảng message mỗi field.
            await httpContext.Response.WriteAsJsonAsync( // Trả validation shape.
                new // Anonymous.
                {
                    code = ApiErrorCodes.ValidationFailed, // Code.
                    type = vex.GetType().Name, // Type name.
                    message = ApiMessages.ValidationFailed, // Summary message.
                    correlationId, // Id.
                    errors, // Chi tiết field.
                    detail = DevDetail(vex) // Dev-only detail.
                },
                cancellationToken); // CT.
            return true; // Handled.
        }

        if (exception is SqlException sqlDirect && TryMapSql(sqlDirect, out var st, out var cd, out var msg)) // SQL lỗi map được.
        {
            _logger.LogWarning(sqlDirect, "SqlException {CorrelationId}", correlationId); // Warning log.
            httpContext.Response.StatusCode = st; // Mapped status.
            await WriteErrorAsync(httpContext, cd, nameof(SqlException), msg, correlationId, sqlDirect, cancellationToken); // JSON.
            return true; // Handled.
        }

        if (exception is DbUpdateException dbEx) // EF save changes failure.
        {
            _logger.LogWarning(dbEx, "DbUpdateException {CorrelationId}", correlationId); // Log.
            if (FindSql(dbEx) is { } sqlInner && TryMapSql(sqlInner, out var st2, out var cd2, out var msg2)) // Inner SqlException.
            {
                httpContext.Response.StatusCode = st2; // Mapped.
                await WriteErrorAsync(httpContext, cd2, dbEx.GetType().Name, msg2, correlationId, dbEx, cancellationToken); // JSON.
                return true; // Handled.
            }

            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest; // Generic DB update fail → 400.
            await WriteErrorAsync( // JSON.
                httpContext, // Ctx.
                ApiErrorCodes.DatabaseUpdateFailed, // Code.
                dbEx.GetType().Name, // Type.
                ApiMessages.InvalidRequest, // Message.
                correlationId, // Correlation.
                dbEx, // Ex.
                cancellationToken); // CT.
            return true; // Handled.
        }

        if (exception is BadHttpRequestException badReq) // Request malformed (size, protocol).
        {
            _logger.LogWarning(badReq, "BadHttpRequest {CorrelationId}", correlationId); // Log.
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest; // 400.
            await WriteErrorAsync( // JSON.
                httpContext, // Ctx.
                ApiErrorCodes.InvalidOperation, // Code bucket.
                badReq.GetType().Name, // Type.
                ApiMessages.InvalidRequest, // Msg.
                correlationId, // Id.
                badReq, // Ex.
                cancellationToken); // CT.
            return true; // Handled.
        }

        if (exception is UnauthorizedAccessException denied) // Chặn truy cập (ít gặp nếu đã map 403 nơi khác).
        {
            _logger.LogWarning(denied, "UnauthorizedAccess {CorrelationId}", correlationId); // Log.
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden; // 403.
            await WriteErrorAsync( // JSON.
                httpContext, // Ctx.
                ApiErrorCodes.Forbidden, // Code.
                denied.GetType().Name, // Type.
                ApiMessages.InsufficientPermission, // Msg.
                correlationId, // Id.
                denied, // Ex.
                cancellationToken); // CT.
            return true; // Handled.
        }

        if (exception is OperationCanceledException cancelled) // Client abort / timeout token.
        {
            _logger.LogWarning(cancelled, "Canceled {CorrelationId}", correlationId); // Log.
            httpContext.Response.StatusCode = StatusCodes.Status408RequestTimeout; // 408 (semantic cancel).
            await WriteErrorAsync( // JSON.
                httpContext, // Ctx.
                ApiErrorCodes.RequestAborted, // Code.
                cancelled.GetType().Name, // Type.
                ApiMessages.RequestCancelled, // Msg.
                correlationId, // Id.
                cancelled, // Ex.
                cancellationToken); // CT.
            return true; // Handled.
        }

        if (exception is FormatException or OverflowException or NotSupportedException) // Đầu vào / thao tác không hỗ trợ.
        {
            _logger.LogWarning(exception, "BadInput {CorrelationId}", correlationId); // Log.
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest; // 400.
            await WriteErrorAsync( // JSON.
                httpContext, // Ctx.
                ApiErrorCodes.InvalidOperation, // Code.
                exception.GetType().Name, // Type.
                ApiMessages.InvalidRequest, // Msg.
                correlationId, // Id.
                exception, // Ex.
                cancellationToken); // CT.
            return true; // Handled.
        }

        if (exception is InvalidOperationException io) // Trạng thái không hợp lệ.
        {
            _logger.LogWarning(io, "InvalidOperation {CorrelationId}", correlationId); // Log.
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest; // 400.
            await WriteErrorAsync( // JSON.
                httpContext, // Ctx.
                ApiErrorCodes.InvalidOperation, // Code.
                io.GetType().Name, // Type.
                ApiMessages.InvalidRequest, // Msg.
                correlationId, // Id.
                io, // Ex.
                cancellationToken); // CT.
            return true; // Handled.
        }

        if (exception is ArgumentException or ArgumentNullException) // Tham số sai.
        {
            _logger.LogWarning(exception, "Argument {CorrelationId}", correlationId); // Log.
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest; // 400.
            await WriteErrorAsync( // JSON.
                httpContext, // Ctx.
                ApiErrorCodes.InvalidOperation, // Code.
                exception.GetType().Name, // Type.
                ApiMessages.InvalidRequest, // Msg.
                correlationId, // Id.
                exception, // Ex.
                cancellationToken); // CT.
            return true; // Handled.
        }

        _logger.LogError(exception, "Unhandled {CorrelationId}", correlationId); // 500 path.
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError; // 500.
        await WriteErrorAsync( // JSON.
            httpContext, // Ctx.
            ApiErrorCodes.InternalError, // Code.
            exception.GetType().Name, // Type.
            ApiMessages.ServerError, // Safe message.
            correlationId, // Id.
            exception, // Ex.
            cancellationToken); // CT.
        return true; // Always handled.
    }

    // Gỡ AggregateException / TargetInvocation để nhánh is phía dưới khớp đúng loại thật.
    private static Exception Unwrap(Exception ex) // Loop unwrap.
    {
        while (true) // Until stable.
        {
            switch (ex) // Pattern match type.
            {
                case AggregateException agg: // Nhiều inner.
                {
                    var inner = agg.Flatten().InnerExceptions.FirstOrDefault(); // Lấy inner đầu.
                    if (inner is null) // Không có inner.
                    {
                        return ex; // Trả agg.
                    }

                    ex = inner; // Drill.
                    continue; // Loop.
                }
                case TargetInvocationException { InnerException: { } i }: // Reflection invoke wrapper.
                    ex = i; // Drill inner.
                    continue; // Loop.
                default: // Không bọc thêm.
                    return ex; // Done.
            }
        }
    }

    private void LogForStatus(int status, Exception ex, string correlationId) // Chọn mức log theo HTTP.
    {
        if (status >= StatusCodes.Status500InternalServerError) // 5xx.
        {
            _logger.LogError(ex, "ApiException {CorrelationId}", correlationId); // Error.
        }
        else // 4xx hoặc khác <500.
        {
            _logger.LogWarning(ex, "ApiException {CorrelationId}", correlationId); // Warning.
        }
    }

    private async Task WriteErrorAsync( // JSON lỗi thống nhất.
        HttpContext http, // Response.
        string code, // Business code string.
        string type, // Exception type name or label.
        string message, // Client-safe message.
        string correlationId, // Correlation.
        Exception ex, // For DevDetail.
        CancellationToken ct) // CT.
    {
        await http.Response.WriteAsJsonAsync( // Serialize.
            new // Shape.
            {
                code, // Code.
                type, // Type.
                message, // Message.
                correlationId, // Correlation.
                detail = DevDetail(ex) // Nullable dev detail.
            },
            ct); // Token.
    }

    // SQL Server: số lỗi → mã + câu trong ApiMessages.cs.
    private static bool TryMapSql(SqlException sql, out int httpStatus, out string code, out string message) // Map known numbers.
    {
        switch (sql.Number) // sql.Number from server.
        {
            case 547: // FK violation.
                httpStatus = StatusCodes.Status400BadRequest; // 400 (client sent bad reference).
                code = ApiErrorCodes.ForeignKeyViolation; // Code.
                message = ApiMessages.ForeignKeyViolation; // Msg.
                return true; // Mapped.
            case 2627: // Unique violation.
            case 2601: // Dup key index.
                httpStatus = StatusCodes.Status409Conflict; // 409.
                code = ApiErrorCodes.DuplicateKey; // Code.
                message = ApiMessages.DuplicateKey; // Msg.
                return true; // Mapped.
            default: // Unknown SQL error number.
                httpStatus = default; // Dummy.
                code = default!; // Null-forgiving.
                message = default!; // Dummy.
                return false; // Not mapped.
        }
    }

    private static SqlException? FindSql(Exception ex) // Walk inner chain.
    {
        for (var e = (Exception?)ex; e != null; e = e.InnerException) // Loop inners.
        {
            if (e is SqlException s) // Found SQL.
            {
                return s; // Return.
            }
        }

        return null; // Not found.
    }

    private string? DevDetail(Exception exception) // Chỉ Development trả chi tiết.
    {
        if (!_environment.IsDevelopment()) // Production: ẩn.
        {
            return null; // No detail.
        }

        var inner = exception.InnerException?.Message; // Inner text.
        return string.IsNullOrEmpty(inner) ? exception.Message : $"{exception.Message} | {inner}"; // Combined.
    }

    private static string? ResolveThrowingDescriptor(Exception exception) // Tìm frame đầu tiên trong namespace CommentAPI.
    {
        foreach (var frame in new StackTrace(exception, fNeedFileInfo: false).GetFrames() ?? Array.Empty<StackFrame>()) // Frames.
        {
            var method = frame.GetMethod(); // Method.
            var declaring = method?.DeclaringType; // Type.
            var ns = declaring?.Namespace; // Namespace string.
            if (ns is null || !ns.StartsWith("CommentAPI", StringComparison.Ordinal)) // Outside our code.
            {
                continue; // Next frame.
            }

            var typeName = declaring!.FullName ?? declaring.Name; // Type name.
            var methodName = method?.Name; // Method name.
            return methodName is null ? typeName : $"{typeName}.{methodName}"; // Descriptor.
        }

        return null; // Fallback handled by caller.
    }
}
