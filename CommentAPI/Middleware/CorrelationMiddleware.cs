using System.Data.Common;
using System.Diagnostics;
using CommentAPI;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace CommentAPI.Middleware;

/// <summary>
/// Gắn correlation id cho mỗi request (header X-Correlation-ID hoặc tự tạo), lưu và trả lại trên response.
/// Đồng thời ghi header hiệu năng: tổng thời gian xử lý request và tổng thời gian lệnh SQL (EF).
/// </summary>
public sealed class CorrelationMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>Thời gian từ lúc request vào pipeline đến khi response bắt đầu gửi (ms).</summary>
    public const string ProcessDurationHeaderName = "X-Process-Duration-Ms";

    /// <summary>Tổng thời gian thực thi lệnh SQL trong request (ms), qua EF Core interceptor.</summary>
    public const string SqlDurationHeaderName = "X-Sql-Duration-Ms";

    /// <summary>Số lần lệnh SQL thực sự gửi tới SQL Server trong request (EF + ADO thô qua <see cref="RecordAdoSqlCommand"/>).</summary>
    public const string SqlQueryCountHeaderName = "X-Sql-Query-Count";

    /// <summary>Key trong <see cref="HttpContext.Items"/> để cộng dồn số lệnh SQL đã thực thi.</summary>
    public const string SqlQueryCountItemKey = "__CommentAPI_SqlQueryCount";

    /// <summary>Backend cache đang dùng: <c>redis</c> hoặc <c>memory</c>.</summary>
    public const string CacheBackendHeaderName = "X-Cache-Backend";

    /// <summary>Trạng thái đọc cache cho request: <c>HIT</c>, <c>MISS</c>, hoặc <c>NONE</c> (không đụng cache).</summary>
    public const string CacheStatusHeaderName = "X-Cache-Status";

    /// <summary>
    /// Header tùy chọn: thành phần API hoặc vị trí (middleware, action, type.method) nơi phát hiện hoặc ném lỗi.
    /// Không nằm trong JSON; vận hành có thể đọc header này cùng X-Correlation-ID.
    /// </summary>
    public const string ErrorSourceHeaderName = "X-CommentAPI-Error-Source";

    public const string ItemKey = "CorrelationId";

    /// <summary>Key trong <see cref="HttpContext.Items"/> để interceptor cộng dồn thời gian SQL.</summary>
    public const string SqlTimingItemKey = "__CommentAPI_SqlTimingMs";

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

        // Đo wall-clock toàn request; header gắn trong OnStarting (trước khi body ghi) để mọi route đều có.
        var wallClock = Stopwatch.StartNew();
        context.Response.OnStarting(OnStartingCallback, (context, wallClock));

        await _next(context).ConfigureAwait(false);
    }

    private static Task OnStartingCallback(object state)
    {
        var (ctx, sw) = ((HttpContext, Stopwatch))state!;
        sw.Stop();

        var cid = ctx.Items[ItemKey]?.ToString();
        if (!string.IsNullOrEmpty(cid) && !ctx.Response.Headers.ContainsKey(HeaderName))
        {
            ctx.Response.Headers.Append(HeaderName, cid);
        }

        if (!ctx.Response.Headers.ContainsKey(ProcessDurationHeaderName))
        {
            ctx.Response.Headers.Append(ProcessDurationHeaderName, sw.ElapsedMilliseconds.ToString());
        }

        var sqlMs = ctx.Items[SqlTimingItemKey] is SqlTimingAccumulator acc ? acc.TotalMilliseconds : 0L;
        if (!ctx.Response.Headers.ContainsKey(SqlDurationHeaderName))
        {
            ctx.Response.Headers.Append(SqlDurationHeaderName, sqlMs.ToString());
        }

        TryAppendSqlQueryCountHeader(ctx);

        // Header cache: backend toàn app + HIT/MISS/NONE cho lần đọc cache (nếu có) trong request.
        var backend = ctx.RequestServices.GetService<CacheBackendDescriptor>();
        if (backend is not null && !ctx.Response.Headers.ContainsKey(CacheBackendHeaderName))
        {
            ctx.Response.Headers.Append(CacheBackendHeaderName, backend.Kind);
        }

        if (!ctx.Response.Headers.ContainsKey(CacheStatusHeaderName))
        {
            var tracker = ctx.RequestServices.GetService<CacheResponseTracker>();
            if (tracker is { LookupPerformed: true })
            {
                ctx.Response.Headers.Append(CacheStatusHeaderName, tracker.WasHit ? "HIT" : "MISS");
            }
            else
            {
                ctx.Response.Headers.Append(CacheStatusHeaderName, "NONE");
            }
        }

        return Task.CompletedTask;
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
    /// Gắn <see cref="SqlQueryCountHeaderName"/> nếu response chưa gửi và header chưa có (dùng cho mọi nhánh trả lời).
    /// </summary>
    public static void TryAppendSqlQueryCountHeader(HttpContext context)
    {
        if (context.Response.HasStarted || context.Response.Headers.ContainsKey(SqlQueryCountHeaderName))
        {
            return;
        }

        var sqlCount = context.Items[SqlQueryCountItemKey] is SqlQueryCountAccumulator qacc ? qacc.Count : 0;
        context.Response.Headers.Append(SqlQueryCountHeaderName, sqlCount.ToString());
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

    /// <summary>
    /// Ghi nhận một lệnh SQL chạy ngoài pipeline EF (ví dụ <c>DbCommand.ExecuteReader</c> trên connection của DbContext).
    /// Mỗi lần gọi = một round-trip tới SQL Server.
    /// </summary>
    public static void RecordAdoSqlCommand(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return;
        }

        IncrementSqlQueryCount(httpContext);
    }

    /// <summary>Tăng bộ đếm lệnh SQL cho request (dùng từ interceptor EF).</summary>
    internal static void IncrementSqlQueryCount(HttpContext httpContext) =>
        GetOrCreateSqlQueryCountAccumulator(httpContext).Increment();

    private static SqlQueryCountAccumulator GetOrCreateSqlQueryCountAccumulator(HttpContext ctx)
    {
        if (ctx.Items[SqlQueryCountItemKey] is SqlQueryCountAccumulator existing)
        {
            return existing;
        }

        lock (ctx.Items)
        {
            if (ctx.Items[SqlQueryCountItemKey] is SqlQueryCountAccumulator e2)
            {
                return e2;
            }

            var created = new SqlQueryCountAccumulator();
            ctx.Items[SqlQueryCountItemKey] = created;
            return created;
        }
    }
}

public static class CorrelationMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationMiddleware>();
}

/// <summary>
/// Cộng dồn <see cref="CommandExecutedEventData.Duration"/> an toàn luồng cho mỗi request.
/// </summary>
internal sealed class SqlTimingAccumulator
{
    private long _milliseconds;

    public void Add(TimeSpan duration)
    {
        var ms = (long)Math.Round(duration.TotalMilliseconds);
        if (ms > 0)
        {
            Interlocked.Add(ref _milliseconds, ms);
        }
    }

    public long TotalMilliseconds => Interlocked.Read(ref _milliseconds);
}

/// <summary>Đếm số lệnh SQL đã thực thi trong một HTTP request (an toàn luồng).</summary>
internal sealed class SqlQueryCountAccumulator
{
    private int _count;

    public void Increment() => Interlocked.Increment(ref _count);

    public int Count => Volatile.Read(ref _count);
}

/// <summary>
/// Interceptor EF: ghi nhận thời gian thực thi lệnh SQL vào <see cref="HttpContext.Items"/> (nếu có HttpContext).
/// </summary>
public sealed class RequestTimingDbCommandInterceptor : DbCommandInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RequestTimingDbCommandInterceptor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private void Track(CommandExecutedEventData eventData)
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null)
        {
            return;
        }

        var acc = GetOrCreateAccumulator(ctx);
        acc.Add(eventData.Duration);

        // Mỗi sự kiện Executed tương ứng một lệnh SQL đã chạy xong qua EF (SELECT/INSERT/UPDATE/…).
        CorrelationMiddleware.IncrementSqlQueryCount(ctx);
    }

    private static SqlTimingAccumulator GetOrCreateAccumulator(HttpContext ctx)
    {
        if (ctx.Items[CorrelationMiddleware.SqlTimingItemKey] is SqlTimingAccumulator existing)
        {
            return existing;
        }

        lock (ctx.Items)
        {
            if (ctx.Items[CorrelationMiddleware.SqlTimingItemKey] is SqlTimingAccumulator e2)
            {
                return e2;
            }

            var created = new SqlTimingAccumulator();
            ctx.Items[CorrelationMiddleware.SqlTimingItemKey] = created;
            return created;
        }
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Track(eventData);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Track(eventData);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Track(eventData);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Track(eventData);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Track(eventData);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        Track(eventData);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }
}
