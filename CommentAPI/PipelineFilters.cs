using System.Diagnostics; // Stopwatch: đo thời gian nhánh Resource → Action → Result.
using CommentAPI.Middleware; // RequestPerformanceMiddleware.GetCorrelationId, header vận hành.
using Microsoft.AspNetCore.Mvc; // IActionResult, ControllerBase-adjacent types.
using Microsoft.AspNetCore.Mvc.Filters; // IAsyncResourceFilter, IAsyncActionFilter, v.v.

namespace CommentAPI;

// =============================================================================
// File PipelineFilters.cs: bốn filter MVC gắn AddControllers — đo thời gian pipeline,
// lưu tên action cho log lỗi, LogWarning khi exception (không nuốt), header loại kết quả + trace cache HIT/MISS.
// =============================================================================

// Khóa HttpContext.Items và tên header dùng chung giữa các filter (tránh typo).
internal static class CommentPipelineFilterKeys
{
    public const string ActionDisplayNameItemKey = "__CommentAPI_ActionDisplayName"; // Tên hiển thị action cho Exception filter.
    public const string HeaderPipelineDurationMs = "X-CommentAPI-Pipeline-Duration-Ms"; // Tổng ms resource+action+result filter nhìn thấy.
    public const string HeaderResultKind = "X-CommentAPI-Result-Type"; // Tên CLR của IActionResult thực tế.
}

// Resource: đo thời gian nhánh pipeline (resource → action → result) → header X-CommentAPI-Pipeline-Duration-Ms.
public sealed class CommentResourceFilter : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    { // Mở khối OnResourceExecutionAsync.
        // BƯỚC 1 — Lấy ILogger theo tên lớp (Serilog route vào FiltersLog.log qua StructuredFileLogger.IsPipelineFilterLog).
        var log = context.HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(nameof(CommentResourceFilter));
        var http = context.HttpContext;

        log?.LogTrace("Resource started {Method} {Path}", http.Request.Method, http.Request.Path);

        // BƯỚC 2 — Đo wall-clock toàn bộ phần còn lại của pipeline (sau delegate next).
        var sw = Stopwatch.StartNew();
        await next();
        sw.Stop();

        // BƯỚC 3 — Gắn header thời lượng nếu response chưa bắt đầu và chưa có header trùng.
        if (!http.Response.HasStarted && !http.Response.Headers.ContainsKey(CommentPipelineFilterKeys.HeaderPipelineDurationMs))
        {
            http.Response.Headers.Append(CommentPipelineFilterKeys.HeaderPipelineDurationMs, sw.ElapsedMilliseconds.ToString());
        }
    } // Kết thúc OnResourceExecutionAsync.
}

// Action: lưu DisplayName vào Items để exception filter / log gắn đúng action.
public sealed class CommentActionFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    { // Mở khối OnActionExecutionAsync.
        // BƯỚC 1 — Logger trace + ghi Items để CommentExceptionFilter đọc được tên action khi throw.
        var log = context.HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(nameof(CommentActionFilter));
        context.HttpContext.Items[CommentPipelineFilterKeys.ActionDisplayNameItemKey] = context.ActionDescriptor.DisplayName;

        log?.LogTrace("Action {Action}", context.ActionDescriptor.DisplayName);
        // BƯỚC 2 — Cho phép action + filter phía sau chạy.
        await next();
    } // Kết thúc OnActionExecutionAsync.
}

// Exception: LogWarning có CorrelationId + Action — không nuốt exception; GlobalExceptionHandler vẫn trả JSON.
public sealed class CommentExceptionFilter : IAsyncExceptionFilter
{
    public Task OnExceptionAsync(ExceptionContext context)
    { // Mở khối OnExceptionAsync.
        // BƯỚC 1 — Gom correlation id + tên action (hoặc placeholder) rồi LogWarning; exception vẫn bubble tới IExceptionHandler.
        var http = context.HttpContext;
        var log = http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(nameof(CommentExceptionFilter));
        var cid = RequestPerformanceMiddleware.GetCorrelationId(http);
        var action = http.Items[CommentPipelineFilterKeys.ActionDisplayNameItemKey]?.ToString() ?? "(action not reached / unknown)";

        log?.LogWarning(
            context.Exception,
            "Error in request pipeline (will be handled by GlobalExceptionHandler). CorrelationId={CorrelationId}, Action={Action}",
            cid,
            action);

        return Task.CompletedTask;
    } // Kết thúc OnExceptionAsync.
}

// Result: header loại IActionResult + log Trace trạng thái entity cache (nếu request có lookup qua CacheResponseTracker).
public sealed class CommentResultFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    { // Mở khối OnResultExecutionAsync.
        var http = context.HttpContext;
        var log = http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(nameof(CommentResultFilter));

        // BƯỚC 1 — Trace loại result trước khi thực thi result (view formatter, serializer, v.v.).
        log?.LogTrace("Result type {Type}", context.Result?.GetType().Name ?? "(null)");
        await next();

        // BƯỚC 2 — Gắn header tên kiểu IActionResult nếu response chưa bắt đầu.
        if (!http.Response.HasStarted
            && context.Result is not null
            && !http.Response.Headers.ContainsKey(CommentPipelineFilterKeys.HeaderResultKind))
        {
            http.Response.Headers.Append(CommentPipelineFilterKeys.HeaderResultKind, context.Result.GetType().Name);
        }

        // BƯỚC 3 — Nếu EntityResponseCache đã lookup trong request — trace HIT/MISS kèm correlation.
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
}
