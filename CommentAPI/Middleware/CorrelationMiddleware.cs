namespace CommentAPI.Middleware;

/// <summary>
/// Gắn correlation id cho mỗi request (header X-Correlation-ID hoặc tự tạo), lưu và trả lại trên response.
/// </summary>
public sealed class CorrelationMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>
    /// Header tùy chọn: thành phần API hoặc vị trí (middleware, action, type.method) nơi phát hiện hoặc ném lỗi.
    /// Không nằm trong JSON; vận hành có thể đọc header này cùng X-Correlation-ID.
    /// </summary>
    public const string ErrorSourceHeaderName = "X-CommentAPI-Error-Source";

    public const string ItemKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id))
        {
            id = Guid.NewGuid().ToString("N");
        }

        context.Items[ItemKey] = id;
        context.Response.OnStarting(static state =>
        {
            var ctx = (HttpContext)state!;
            var cid = ctx.Items[ItemKey]?.ToString();
            if (!string.IsNullOrEmpty(cid) && !ctx.Response.Headers.ContainsKey(HeaderName))
            {
                ctx.Response.Headers.Append(HeaderName, cid);
            }

            return Task.CompletedTask;
        }, context);

        await _next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Lấy correlation id của request hiện tại; tạo và lưu nếu chưa có.
    /// </summary>
    public static string GetCorrelationId(HttpContext context)
    {
        if (context.Items.TryGetValue(ItemKey, out var v) && v is string s && !string.IsNullOrEmpty(s))
        {
            return s;
        }

        var id = Guid.NewGuid().ToString("N");
        context.Items[ItemKey] = id;
        return id;
    }

    /// <summary>
    /// Gắn X-CommentAPI-Error-Source tối đa một lần trước khi response bắt đầu.
    /// </summary>
    public static void AppendErrorSourceHeader(HttpContext context, string? errorSource)
    {
        if (string.IsNullOrWhiteSpace(errorSource) || context.Response.HasStarted)
        {
            return;
        }

        if (!context.Response.Headers.ContainsKey(ErrorSourceHeaderName))
        {
            context.Response.Headers.Append(ErrorSourceHeaderName, errorSource.Trim());
        }
    }
}

public static class CorrelationMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationMiddleware>();
}
