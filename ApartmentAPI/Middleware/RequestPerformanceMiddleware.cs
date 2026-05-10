using System.Data.Common; // DbCommand interceptor.
using System.Diagnostics; // Stopwatch wall clock.
using ApartmentAPI; // CacheBackendDescriptor từ AddDistributedCaching.
using Microsoft.AspNetCore.Http; // HttpContext.Items, Headers, OnStarting.
using Microsoft.EntityFrameworkCore.Diagnostics; // DbCommandInterceptor, CommandExecutedEventData.
using Microsoft.Extensions.DependencyInjection; // GetService CacheResponseTracker.

namespace ApartmentAPI.Middleware;

// Gắn correlation id + đo tổng thời gian xử lí + tổng thời gian SQL và số query; ghi header cho client / downstream.
public sealed class RequestPerformanceMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ProcessDurationHeaderName = "X-Process-Duration-Ms";
    public const string SqlDurationHeaderName = "X-Sql-Duration-Ms";
    public const string SqlQueryCountHeaderName = "X-Sql-Query-Count";
    public const string SqlQueryCountItemKey = "__ApartmentAPI_SqlQueryCount";
    public const string CacheBackendHeaderName = "X-Cache-Backend";
    public const string CacheStatusHeaderName = "X-Cache-Status";
    public const string ErrorSourceHeaderName = "X-ApartmentAPI-Error-Source";
    public const string ItemKey = "CorrelationId";
    public const string SqlTimingItemKey = "__ApartmentAPI_SqlTimingMs";

    private readonly RequestDelegate _next; // Bước kế trong pipeline.

    public RequestPerformanceMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context) // Mỗi request: khởi tạo correlation + callback OnStarting.
    {
        var id = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id))
            id = Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = id;

        var wallClock = Stopwatch.StartNew();
        context.Response.OnStarting(OnStartingCallback, (context, wallClock));

        await _next(context).ConfigureAwait(false);
    }

    private static Task OnStartingCallback(object state) // Chạy ngay trước khi headers response flush — chắc chắn ghi được custom header.
    {
        var (ctx, sw) = ((HttpContext, Stopwatch))state!;
        sw.Stop();

        var cid = ctx.Items[ItemKey]?.ToString();
        if (!string.IsNullOrEmpty(cid) && !ctx.Response.Headers.ContainsKey(HeaderName))
            ctx.Response.Headers.Append(HeaderName, cid);

        if (!ctx.Response.Headers.ContainsKey(ProcessDurationHeaderName))
            ctx.Response.Headers.Append(ProcessDurationHeaderName, sw.ElapsedMilliseconds.ToString());

        var sqlMs = ctx.Items[SqlTimingItemKey] is SqlTimingAccumulator acc ? acc.TotalMilliseconds : 0L;
        if (!ctx.Response.Headers.ContainsKey(SqlDurationHeaderName))
            ctx.Response.Headers.Append(SqlDurationHeaderName, sqlMs.ToString());

        TryAppendSqlQueryCountHeader(ctx);

        var backend = ctx.RequestServices.GetService<CacheBackendDescriptor>();
        if (backend is not null && !ctx.Response.Headers.ContainsKey(CacheBackendHeaderName))
            ctx.Response.Headers.Append(CacheBackendHeaderName, backend.Kind);

        if (!ctx.Response.Headers.ContainsKey(CacheStatusHeaderName))
        {
            var tracker = ctx.RequestServices.GetService<CacheResponseTracker>();
            if (tracker is { LookupPerformed: true })
                ctx.Response.Headers.Append(CacheStatusHeaderName, tracker.WasHit ? "HIT" : "MISS");
            else
                ctx.Response.Headers.Append(CacheStatusHeaderName, "NONE");
        }

        return Task.CompletedTask;
    }

    public static string GetCorrelationId(HttpContext context) // Đọc từ Items hoặc sinh Guid mới (middleware chưa chạy).
    {
        if (context.Items.TryGetValue(ItemKey, out var v) && v is string s && !string.IsNullOrEmpty(s))
            return s;

        var id = Guid.NewGuid().ToString("N");
        context.Items[ItemKey] = id;
        return id;
    }

    public static void TryAppendSqlQueryCountHeader(HttpContext context)
    {
        if (context.Response.HasStarted || context.Response.Headers.ContainsKey(SqlQueryCountHeaderName))
            return;

        var sqlCount = context.Items[SqlQueryCountItemKey] is SqlQueryCountAccumulator qacc ? qacc.Count : 0;
        context.Response.Headers.Append(SqlQueryCountHeaderName, sqlCount.ToString());
    }

    public static void AppendErrorSourceHeader(HttpContext context, string? errorSource)
    {
        if (string.IsNullOrWhiteSpace(errorSource) || context.Response.HasStarted)
            return;

        if (!context.Response.Headers.ContainsKey(ErrorSourceHeaderName))
            context.Response.Headers.Append(ErrorSourceHeaderName, errorSource.Trim());
    }

    public static void RecordAdoSqlCommand(HttpContext? httpContext)
    {
        if (httpContext is null)
            return;

        IncrementSqlQueryCount(httpContext);
    }

    internal static void IncrementSqlQueryCount(HttpContext httpContext) =>
        GetOrCreateSqlQueryCountAccumulator(httpContext).Increment();

    private static SqlQueryCountAccumulator GetOrCreateSqlQueryCountAccumulator(HttpContext ctx)
    {
        if (ctx.Items[SqlQueryCountItemKey] is SqlQueryCountAccumulator existing)
            return existing;

        lock (ctx.Items)
        {
            if (ctx.Items[SqlQueryCountItemKey] is SqlQueryCountAccumulator e2)
                return e2;

            var created = new SqlQueryCountAccumulator();
            ctx.Items[SqlQueryCountItemKey] = created;
            return created;
        }
    }
}

public static class RequestPerformanceMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestPerformance(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestPerformanceMiddleware>();
}

internal sealed class SqlTimingAccumulator // Cộng dồn ms từ mỗi ReaderExecuted/NonQuery/Scalar.
{
    private long _milliseconds;

    public void Add(TimeSpan duration)
    {
        var ms = (long)Math.Round(duration.TotalMilliseconds);
        if (ms > 0)
            Interlocked.Add(ref _milliseconds, ms);
    }

    public long TotalMilliseconds => Interlocked.Read(ref _milliseconds);
}

internal sealed class SqlQueryCountAccumulator // Đếm số round-trip SQL trong một HTTP request.
{
    private int _count;

    public void Increment() => Interlocked.Increment(ref _count);

    public int Count => Volatile.Read(ref _count);
}

public sealed class RequestTimingDbCommandInterceptor : DbCommandInterceptor // Đăng ký singleton + AddInterceptors trên DbContext.
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
            return;

        var acc = GetOrCreateAccumulator(ctx);
        acc.Add(eventData.Duration);
        RequestPerformanceMiddleware.IncrementSqlQueryCount(ctx);
    }

    private static SqlTimingAccumulator GetOrCreateAccumulator(HttpContext ctx)
    {
        if (ctx.Items[RequestPerformanceMiddleware.SqlTimingItemKey] is SqlTimingAccumulator existing)
            return existing;

        lock (ctx.Items)
        {
            if (ctx.Items[RequestPerformanceMiddleware.SqlTimingItemKey] is SqlTimingAccumulator e2)
                return e2;

            var created = new SqlTimingAccumulator();
            ctx.Items[RequestPerformanceMiddleware.SqlTimingItemKey] = created;
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
