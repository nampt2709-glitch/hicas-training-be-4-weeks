using ApartmentAPI.Data;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApartmentAPI.Middleware;

// Bắt exception toàn pipeline; ApiException → JSON có status/code/message.
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is ApiException api)
        {
            _logger.LogWarning(exception, "ApiException {Code}", api.ErrorCode);
            httpContext.Response.StatusCode = api.StatusCode;
            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsJsonAsync(new
            {
                code = api.ErrorCode,
                message = api.ClientMessage,
                detail = _env.IsDevelopment() ? api.ToString() : null,
            }, cancellationToken);
            return true;
        }

        if (exception is DbUpdateException db)
        {
            _logger.LogError(db, "DbUpdateException");
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsJsonAsync(new
            {
                code = "DATABASE",
                message = "Update failed.",
                detail = _env.IsDevelopment() ? db.InnerException?.Message ?? db.Message : null,
            }, cancellationToken);
            return true;
        }

        _logger.LogError(exception, "Unhandled");
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsJsonAsync(new
        {
            code = "INTERNAL",
            message = "An error occurred.",
            detail = _env.IsDevelopment() ? exception.ToString() : null,
        }, cancellationToken);
        return true;
    }
}
