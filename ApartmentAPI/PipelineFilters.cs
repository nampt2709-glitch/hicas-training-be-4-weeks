using System.Diagnostics; // Stopwatch đo pipeline.
using ApartmentAPI.Middleware; // RequestPerformanceMiddleware correlation id.
using Microsoft.AspNetCore.Mvc; // IActionResult, filters.
using Microsoft.AspNetCore.Mvc.Filters; // IAsync*Filter.

namespace ApartmentAPI;

// Khóa HttpContext.Items và header response — cùng pattern CommentAPI (prefix ApartmentAPI).
internal static class ApartmentPipelineFilterKeys
{ // Mở khối ApartmentPipelineFilterKeys.
    public const string ActionDisplayNameItemKey = "__ApartmentAPI_ActionDisplayName"; // DisplayName action cho log lỗi.
    public const string HeaderPipelineDurationMs = "X-ApartmentAPI-Pipeline-Duration-Ms"; // Thời gian nhánh resource.
    public const string HeaderResultKind = "X-ApartmentAPI-Result-Type"; // Tên CLR của IActionResult.
} // Kết thúc ApartmentPipelineFilterKeys.

// Resource: đo thời gian nhánh pipeline → header X-ApartmentAPI-Pipeline-Duration-Ms.
public sealed class ApartmentResourceFilter : IAsyncResourceFilter
{ // Mở khối ApartmentResourceFilter.
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    { // Mở khối OnResourceExecutionAsync.
        // BƯỚC 1 — Logger category theo tên filter (+ Trace start).
        var log = context.HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(nameof(ApartmentResourceFilter));
        var http = context.HttpContext;

        log?.LogTrace("Resource started {Method} {Path}", http.Request.Method, http.Request.Path);

        // BƯỚC 2 — Đo thời gian toàn bộ pipeline resource→result sau await next.
        var sw = Stopwatch.StartNew();
        await next(); // Chạy controller + action + result.
        sw.Stop();

        // BƯỚC 3 — Gắn header ms nếu response chưa bắt đầu và chưa có header.
        if (!http.Response.HasStarted && !http.Response.Headers.ContainsKey(ApartmentPipelineFilterKeys.HeaderPipelineDurationMs))
            http.Response.Headers.Append(ApartmentPipelineFilterKeys.HeaderPipelineDurationMs, sw.ElapsedMilliseconds.ToString());
    } // Kết thúc OnResourceExecutionAsync.
} // Kết thúc ApartmentResourceFilter.

// Action: lưu DisplayName để exception filter / log gắn đúng action.
public sealed class ApartmentActionFilter : IAsyncActionFilter
{ // Mở khối ApartmentActionFilter.
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    { // Mở khối OnActionExecutionAsync.
        // BƯỚC 1 — Logger Trace.
        var log = context.HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(nameof(ApartmentActionFilter));
        // BƯỚC 2 — Lưu DisplayName vào Items cho exception path.
        context.HttpContext.Items[ApartmentPipelineFilterKeys.ActionDisplayNameItemKey] = context.ActionDescriptor.DisplayName;

        log?.LogTrace("Action {Action}", context.ActionDescriptor.DisplayName);
        await next(); // Thực thi action + filters sau.
    } // Kết thúc OnActionExecutionAsync.
} // Kết thúc ApartmentActionFilter.

// Exception: LogWarning + CorrelationId + Action — không nuốt exception; GlobalExceptionHandler trả JSON.
public sealed class ApartmentExceptionFilter : IAsyncExceptionFilter
{ // Mở khối ApartmentExceptionFilter.
    public Task OnExceptionAsync(ExceptionContext context)
    { // Mở khối OnExceptionAsync.
        // BƯỚC 1 — Lấy HttpContext, logger, correlationId, tên action từ Items.
        var http = context.HttpContext;
        var log = http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(nameof(ApartmentExceptionFilter));
        var cid = RequestPerformanceMiddleware.GetCorrelationId(http);
        var action = http.Items[ApartmentPipelineFilterKeys.ActionDisplayNameItemKey]?.ToString() ?? "(action not reached / unknown)";

        // BƯỚC 2 — Warning có exception — middleware sau vẫn xử lý response.
        log?.LogWarning(
            context.Exception,
            "Error in request pipeline (handled by GlobalExceptionHandler). CorrelationId={CorrelationId}, Action={Action}",
            cid,
            action);

        return Task.CompletedTask; // Không set ExceptionHandled — để GlobalExceptionHandler.
    } // Kết thúc OnExceptionAsync.
} // Kết thúc ApartmentExceptionFilter.

// Result: header loại IActionResult + log Trace cache entity (CacheResponseTracker).
public sealed class ApartmentResultFilter : IAsyncResultFilter
{ // Mở khối ApartmentResultFilter.
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    { // Mở khối OnResultExecutionAsync.
        // BƯỚC 1 — Trace loại kết quả trước khi render.
        var http = context.HttpContext;
        var log = http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(nameof(ApartmentResultFilter));

        log?.LogTrace("Result type {Type}", context.Result?.GetType().Name ?? "(null)");
        await next(); // Thực thi result execution (serialization, v.v.).

        // BƯỚC 2 — Header loại IActionResult nếu còn thêm header được.
        if (!http.Response.HasStarted
            && context.Result is not null
            && !http.Response.Headers.ContainsKey(ApartmentPipelineFilterKeys.HeaderResultKind))
        {
            http.Response.Headers.Append(ApartmentPipelineFilterKeys.HeaderResultKind, context.Result.GetType().Name);
        }

        // BƯỚC 3 — Nếu có lookup cache entity trong request: log HIT/MISS.
        var tracker = http.RequestServices.GetService<CacheResponseTracker>();
        if (tracker is { LookupPerformed: true })
        {
            var cid = RequestPerformanceMiddleware.GetCorrelationId(http);
            log?.LogTrace(
                "Entity response cache: {CacheOutcome} (CorrelationId={CorrelationId})",
                tracker.WasHit ? "HIT" : "MISS",
                cid);
        }
    } // Kết thúc OnResultExecutionAsync.
} // Kết thúc ApartmentResultFilter.
