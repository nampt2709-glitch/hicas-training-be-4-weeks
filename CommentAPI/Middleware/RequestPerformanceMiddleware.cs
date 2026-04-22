using System.Data.Common;
using System.Diagnostics; // Stopwatch đo thời gian.
using CommentAPI;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace CommentAPI.Middleware;

// Middleware đo lường hiệu năng request: thời gian xử lý wall-clock, tổng thời gian SQL (EF), số round-trip SQL, trạng thái cache.
// Đồng thời gắn correlation id (header X-Correlation-ID hoặc tự tạo), lưu HttpContext.Items và echo trên response.
public sealed class RequestPerformanceMiddleware // Pipeline sớm: tracing + metrics header + cache hint.
{
    public const string HeaderName = "X-Correlation-ID"; // Header chuẩn correlation.

    // Thời gian từ lúc request vào pipeline đến khi response bắt đầu gửi (ms).
    public const string ProcessDurationHeaderName = "X-Process-Duration-Ms"; // Wall-clock request.

    // Tổng thời gian thực thi lệnh SQL trong request (ms), qua EF Core interceptor.
    public const string SqlDurationHeaderName = "X-Sql-Duration-Ms"; // Sum SQL duration.

    // Số lần lệnh SQL thực sự gửi tới SQL Server trong request (EF + ADO thô qua RecordAdoSqlCommand).
    public const string SqlQueryCountHeaderName = "X-Sql-Query-Count"; // Round-trip count.

    // Key trong HttpContext.Items để cộng dồn số lệnh SQL đã thực thi.
    public const string SqlQueryCountItemKey = "__CommentAPI_SqlQueryCount"; // Item key.

    // Backend cache đang dùng: redis hoặc memory.
    public const string CacheBackendHeaderName = "X-Cache-Backend"; // Which backend served.

    // Trạng thái đọc cache cho request: HIT, MISS, hoặc NONE (không đụng cache).
    public const string CacheStatusHeaderName = "X-Cache-Status"; // Cache lookup outcome.

    // Header tùy chọn: thành phần API hoặc vị trí (middleware, action, type.method) nơi phát hiện hoặc ném lỗi.
    // Không nằm trong JSON; vận hành có thể đọc header này cùng X-Correlation-ID.
    public const string ErrorSourceHeaderName = "X-CommentAPI-Error-Source"; // Ops header.

    public const string ItemKey = "CorrelationId"; // Items dictionary key for id string.

    // Key trong HttpContext.Items để interceptor cộng dồn thời gian SQL.
    public const string SqlTimingItemKey = "__CommentAPI_SqlTimingMs"; // Accumulator key.

    private readonly RequestDelegate _next; // Next middleware.

    public RequestPerformanceMiddleware(RequestDelegate next) => _next = next; // Primary ctor expression body.

    public async Task InvokeAsync(HttpContext context) // Per-request.
    {
        var id = context.Request.Headers[HeaderName].FirstOrDefault(); // Client-supplied id optional.
        if (string.IsNullOrWhiteSpace(id)) // Missing/blank.
        {
            id = Guid.NewGuid().ToString("N"); // Generate compact guid.
        }

        context.Items[ItemKey] = id; // Store for downstream.

        // Đo wall-clock toàn request; header gắn trong OnStarting (trước khi body ghi) để mọi route đều có.
        var wallClock = Stopwatch.StartNew(); // Start timer.
        context.Response.OnStarting(OnStartingCallback, (context, wallClock)); // Hook before headers sent.

        await _next(context).ConfigureAwait(false); // Continue pipeline.
    }

    private static Task OnStartingCallback(object state) // Called by server before response start.
    {
        var (ctx, sw) = ((HttpContext, Stopwatch))state!; // Unpack state tuple.
        sw.Stop(); // Stop watch.

        var cid = ctx.Items[ItemKey]?.ToString(); // Read correlation string.
        if (!string.IsNullOrEmpty(cid) && !ctx.Response.Headers.ContainsKey(HeaderName)) // Avoid duplicate.
        {
            ctx.Response.Headers.Append(HeaderName, cid); // Echo correlation.
        }

        if (!ctx.Response.Headers.ContainsKey(ProcessDurationHeaderName)) // Process duration once.
        {
            ctx.Response.Headers.Append(ProcessDurationHeaderName, sw.ElapsedMilliseconds.ToString()); // Ms elapsed.
        }

        var sqlMs = ctx.Items[SqlTimingItemKey] is SqlTimingAccumulator acc ? acc.TotalMilliseconds : 0L; // Sum SQL ms.
        if (!ctx.Response.Headers.ContainsKey(SqlDurationHeaderName)) // SQL duration once.
        {
            ctx.Response.Headers.Append(SqlDurationHeaderName, sqlMs.ToString()); // Total SQL ms.
        }

        TryAppendSqlQueryCountHeader(ctx); // Ensure count header.

        // Header cache: backend toàn app + HIT/MISS/NONE cho lần đọc cache (nếu có) trong request.
        var backend = ctx.RequestServices.GetService<CacheBackendDescriptor>(); // Singleton descriptor.
        if (backend is not null && !ctx.Response.Headers.ContainsKey(CacheBackendHeaderName)) // If resolved.
        {
            ctx.Response.Headers.Append(CacheBackendHeaderName, backend.Kind); // redis/memory.
        }

        if (!ctx.Response.Headers.ContainsKey(CacheStatusHeaderName)) // Cache status once.
        {
            var tracker = ctx.RequestServices.GetService<CacheResponseTracker>(); // Scoped tracker.
            if (tracker is { LookupPerformed: true }) // Cache layer touched.
            {
                ctx.Response.Headers.Append(CacheStatusHeaderName, tracker.WasHit ? "HIT" : "MISS"); // Hit flag.
            }
            else // No lookup.
            {
                ctx.Response.Headers.Append(CacheStatusHeaderName, "NONE"); // Explicit none.
            }
        }

        return Task.CompletedTask; // Sync completion for OnStarting delegate.
    }

    // Lấy correlation id của request hiện tại; tạo và lưu nếu chưa có.
    public static string GetCorrelationId(HttpContext context) // Public helper.
    {
        if (context.Items.TryGetValue(ItemKey, out var v) && v is string s && !string.IsNullOrEmpty(s)) // Already set.
        {
            return s; // Return existing.
        }

        var id = Guid.NewGuid().ToString("N"); // New id fallback.
        context.Items[ItemKey] = id; // Store.
        return id; // Return.
    }

    // Gắn SqlQueryCountHeaderName nếu response chưa gửi và header chưa có (dùng cho mọi nhánh trả lời).
    public static void TryAppendSqlQueryCountHeader(HttpContext context) // Idempotent append.
    {
        if (context.Response.HasStarted || context.Response.Headers.ContainsKey(SqlQueryCountHeaderName)) // Too late or exists.
        {
            return; // No-op.
        }

        var sqlCount = context.Items[SqlQueryCountItemKey] is SqlQueryCountAccumulator qacc ? qacc.Count : 0; // Read count.
        context.Response.Headers.Append(SqlQueryCountHeaderName, sqlCount.ToString()); // Append int string.
    }

    // Gắn X-CommentAPI-Error-Source tối đa một lần trước khi response bắt đầu.
    public static void AppendErrorSourceHeader(HttpContext context, string? errorSource) // Ops tracing.
    {
        if (string.IsNullOrWhiteSpace(errorSource) || context.Response.HasStarted) // Invalid or flushed.
        {
            return; // Skip.
        }

        if (!context.Response.Headers.ContainsKey(ErrorSourceHeaderName)) // First writer wins.
        {
            context.Response.Headers.Append(ErrorSourceHeaderName, errorSource.Trim()); // Trim whitespace.
        }
    }

    // Ghi nhận một lệnh SQL chạy ngoài pipeline EF (ví dụ DbCommand.ExecuteReader trên connection của DbContext).
    // Mỗi lần gọi = một round-trip tới SQL Server.
    public static void RecordAdoSqlCommand(HttpContext? httpContext) // ADO manual tracking.
    {
        if (httpContext is null) // No HTTP scope.
        {
            return; // Skip.
        }

        IncrementSqlQueryCount(httpContext); // Bump counter.
    }

    // Tăng bộ đếm lệnh SQL cho request (dùng từ interceptor EF).
    internal static void IncrementSqlQueryCount(HttpContext httpContext) => // EF interceptor entry.
        GetOrCreateSqlQueryCountAccumulator(httpContext).Increment(); // Thread-safe increment.

    private static SqlQueryCountAccumulator GetOrCreateSqlQueryCountAccumulator(HttpContext ctx) // Lazy item.
    {
        if (ctx.Items[SqlQueryCountItemKey] is SqlQueryCountAccumulator existing) // Fast path.
        {
            return existing; // Existing.
        }

        lock (ctx.Items) // Serialize creation on same Items bag.
        {
            if (ctx.Items[SqlQueryCountItemKey] is SqlQueryCountAccumulator e2) // Double-check.
            {
                return e2; // Created by other thread.
            }

            var created = new SqlQueryCountAccumulator(); // New accumulator.
            ctx.Items[SqlQueryCountItemKey] = created; // Store.
            return created; // Return new.
        }
    }
}

public static class RequestPerformanceMiddlewareExtensions // Đăng ký middleware trong pipeline.
{
    // Bật middleware hiệu năng + correlation: header thời gian, SQL, cache, X-Correlation-ID.
    public static IApplicationBuilder UseRequestPerformance(this IApplicationBuilder app) => // Extension.
        app.UseMiddleware<RequestPerformanceMiddleware>(); // Add middleware.
}

// Cộng dồn CommandExecutedEventData.Duration an toàn luồng cho mỗi request.
internal sealed class SqlTimingAccumulator // Interlocked sum.
{
    private long _milliseconds; // Backing field.

    public void Add(TimeSpan duration) // Add non-negative ms.
    {
        var ms = (long)Math.Round(duration.TotalMilliseconds); // Round to long.
        if (ms > 0) // Ignore zero/negative noise.
        {
            Interlocked.Add(ref _milliseconds, ms); // Atomic add.
        }
    }

    public long TotalMilliseconds => Interlocked.Read(ref _milliseconds); // Volatile read pattern via Interlocked.Read.
}

// Đếm số lệnh SQL đã thực thi trong một HTTP request (an toàn luồng).
internal sealed class SqlQueryCountAccumulator // Interlocked count.
{
    private int _count; // Backing.

    public void Increment() => Interlocked.Increment(ref _count); // ++ atomic.

    public int Count => Volatile.Read(ref _count); // Read fresh.
}

// Interceptor EF: ghi nhận thời gian thực thi lệnh SQL vào HttpContext.Items (nếu có HttpContext).
public sealed class RequestTimingDbCommandInterceptor : DbCommandInterceptor // Hook EF commands.
{
    private readonly IHttpContextAccessor _httpContextAccessor; // Resolve current HttpContext.

    public RequestTimingDbCommandInterceptor(IHttpContextAccessor httpContextAccessor) // DI.
    {
        _httpContextAccessor = httpContextAccessor; // Store accessor.
    }

    private void Track(CommandExecutedEventData eventData) // Common track path.
    {
        var ctx = _httpContextAccessor.HttpContext; // May be null outside request.
        if (ctx is null) // No HTTP.
        {
            return; // Skip timing.
        }

        var acc = GetOrCreateAccumulator(ctx); // Timing accumulator.
        acc.Add(eventData.Duration); // Add duration.

        // Mỗi sự kiện Executed tương ứng một lệnh SQL đã chạy xong qua EF (SELECT/INSERT/UPDATE/…).
        RequestPerformanceMiddleware.IncrementSqlQueryCount(ctx); // Also count command.
    }

    private static SqlTimingAccumulator GetOrCreateAccumulator(HttpContext ctx) // Lazy per request.
    {
        if (ctx.Items[RequestPerformanceMiddleware.SqlTimingItemKey] is SqlTimingAccumulator existing) // Fast path.
        {
            return existing; // Found.
        }

        lock (ctx.Items) // Serialize.
        {
            if (ctx.Items[RequestPerformanceMiddleware.SqlTimingItemKey] is SqlTimingAccumulator e2) // Double-check.
            {
                return e2; // Other thread created.
            }

            var created = new SqlTimingAccumulator(); // New.
            ctx.Items[RequestPerformanceMiddleware.SqlTimingItemKey] = created; // Store.
            return created; // Return.
        }
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result) // Sync reader.
    {
        Track(eventData); // Record.
        return base.ReaderExecuted(command, eventData, result); // Base.
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync( // Async reader.
        DbCommand command, // Command.
        CommandExecutedEventData eventData, // Meta.
        DbDataReader result, // Reader.
        CancellationToken cancellationToken = default) // CT.
    {
        Track(eventData); // Record.
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken); // Base.
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result) // Sync non-query.
    {
        Track(eventData); // Record.
        return base.NonQueryExecuted(command, eventData, result); // Base.
    }

    public override ValueTask<int> NonQueryExecutedAsync( // Async non-query.
        DbCommand command, // Cmd.
        CommandExecutedEventData eventData, // Meta.
        int result, // Rows.
        CancellationToken cancellationToken = default) // CT.
    {
        Track(eventData); // Record.
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken); // Base.
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result) // Sync scalar.
    {
        Track(eventData); // Record.
        return base.ScalarExecuted(command, eventData, result); // Base.
    }

    public override ValueTask<object?> ScalarExecutedAsync( // Async scalar.
        DbCommand command, // Cmd.
        CommandExecutedEventData eventData, // Meta.
        object? result, // Scalar.
        CancellationToken cancellationToken = default) // CT.
    {
        Track(eventData); // Record.
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken); // Base.
    }
}
