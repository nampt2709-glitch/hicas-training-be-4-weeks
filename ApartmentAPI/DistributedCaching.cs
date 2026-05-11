using System.Text.Json; // Serialize/Deserialize DTO cache.
using Microsoft.AspNetCore.Builder; // WebApplicationBuilder.
using Microsoft.Extensions.Caching.Distributed; // IDistributedCache, options.
using Microsoft.Extensions.Caching.Memory; // MemoryDistributedCache fallback.
using Microsoft.Extensions.Caching.StackExchangeRedis; // RedisCache.
using Microsoft.Extensions.Logging; // ILoggerFactory probe + wiring.
using Microsoft.Extensions.Options; // IOptions<T> cho TTL.
using MSOptions = Microsoft.Extensions.Options.Options; // Factory MemoryDistributedCacheOptions.
using StackExchange.Redis; // RedisException cho failover.

namespace ApartmentAPI;

#region Tuỳ chọn và trạng thái cache

// Cache phân tán Redis-first + memory failover; JSON entity cache (cùng mô hình CommentAPI).
public sealed class CacheOptions
{ // Mở khối CacheOptions — bind từ appsettings section "Cache".
    public const string SectionName = "Cache"; // Tên section cấu hình.
    public int EntityTtlSeconds { get; set; } = 120; // TTL mặc định cho GetById JSON.
    public bool PreferInProcessCache { get; set; } = false; // true = bỏ Redis, chỉ memory.
    public int RedisProbeTimeoutMilliseconds { get; set; } = 5_000; // Timeout thăm dò (dự phòng).
}

// Singleton: backend hiện tại "redis" hoặc "memory" — health/Swagger có thể đọc.
public sealed class CacheBackendState
{ // Mở khối CacheBackendState.
    private string _kind = "memory"; // Mặc định memory cho đến khi Redis thành công.
    public string Kind { get => _kind; set => _kind = value; } // Loại backend đang phục vụ đọc/ghi.
}

// Theo dõi một request có lookup cache entity hay không + hit/miss — ResultFilter log Trace.
public sealed class CacheResponseTracker
{ // Mở khối CacheResponseTracker.
    public bool LookupPerformed { get; private set; } // Đã gọi GetJson ít nhất một lần.
    public bool WasHit { get; private set; } // Lần lookup cuối là hit.

    // Ghi nhận kết quả lookup cho middleware/filter.
    public void ReportLookup(bool cacheHit)
    { // Mở khối ReportLookup.
        LookupPerformed = true; // Có hoạt động cache trong request.
        WasHit = cacheHit; // true = deserialize thành công.
    } // Kết thúc ReportLookup.
} // Kết thúc CacheResponseTracker.

#endregion

#region Khóa cache và giao diện entity cache

// Khóa cache chi tiết entity ApartmentAPI (chỉ GetById; invalidation khi CUD).
public static class EntityCacheKeys
{ // Mở khối EntityCacheKeys — factory chuỗi khóa ngắn gọn.
    public static string User(Guid id) => $"apt:u:{id:N}"; // User theo Id.
    public static string Apartment(Guid id) => $"apt:a:{id:N}"; // Căn hộ.
    public static string Resident(Guid id) => $"apt:r:{id:N}"; // Cư dân.
    public static string UtilityService(Guid id) => $"apt:util:{id:N}"; // Tiện ích.
    public static string Invoice(Guid id) => $"apt:inv:{id:N}"; // Hóa đơn.
    public static string InvoiceItem(Guid id) => $"apt:invit:{id:N}"; // Dòng hóa đơn.
    public static string Feedback(Guid id) => $"apt:fb:{id:N}"; // Phản hồi.
    public static string Post(Guid id) => $"apt:post:{id:N}"; // Bài đăng.
    public static string Attachment(Guid id) => $"apt:att:{id:N}"; // Đính kèm.
    public static string RefreshToken(Guid id) => $"apt:rt:{id:N}"; // Refresh token.
    public static string Role(Guid id) => $"apt:role:{id:N}"; // Role.

    // Danh sách phân trang (epoch bump khi CUD làm lỗi thời mọi trang/sort).
    public static string ApartmentsPaged(long epoch, int page, int pageSize, int sortSeg, bool desc) =>
        $"apt:{epoch}:l:a:{page}:{pageSize}:s{sortSeg}:{(desc ? 1 : 0)}";

    public static string ResidentsPaged(long epoch, int page, int pageSize, int sortSeg, bool desc) =>
        $"apt:{epoch}:l:r:{page}:{pageSize}:s{sortSeg}:{(desc ? 1 : 0)}";

    public static string UtilitiesPaged(long epoch, int page, int pageSize, int sortSeg, bool desc) =>
        $"apt:{epoch}:l:u:{page}:{pageSize}:s{sortSeg}:{(desc ? 1 : 0)}";

    public static string InvoicesPaged(long epoch, int page, int pageSize, int sortSeg, bool desc) =>
        $"apt:{epoch}:l:i:{page}:{pageSize}:s{sortSeg}:{(desc ? 1 : 0)}";

    public static string InvoiceItemsPaged(long epoch, int page, int pageSize, int sortSeg, bool desc) =>
        $"apt:{epoch}:l:ii:{page}:{pageSize}:s{sortSeg}:{(desc ? 1 : 0)}";

    public static string FeedbacksPaged(long epoch, int page, int pageSize, int sortSeg, bool desc) =>
        $"apt:{epoch}:l:f:{page}:{pageSize}:s{sortSeg}:{(desc ? 1 : 0)}";

    public static string PostsPaged(long epoch, int page, int pageSize, int sortSeg, bool desc) =>
        $"apt:{epoch}:l:pst:{page}:{pageSize}:s{sortSeg}:{(desc ? 1 : 0)}";

    public static string AttachmentsPaged(long epoch, int page, int pageSize, int sortSeg, bool desc) =>
        $"apt:{epoch}:l:at:{page}:{pageSize}:s{sortSeg}:{(desc ? 1 : 0)}";

    public static string RefreshTokensPaged(long epoch, int page, int pageSize, int sortSeg, bool desc) =>
        $"apt:{epoch}:l:rt:{page}:{pageSize}:s{sortSeg}:{(desc ? 1 : 0)}";

    public static string UsersPaged(long epoch, int page, int pageSize, int sortSeg, bool desc) =>
        $"apt:{epoch}:l:usr:{page}:{pageSize}:s{sortSeg}:{(desc ? 1 : 0)}";

    public static string RolesPaged(long epoch, int page, int pageSize, int sortSeg, bool desc) =>
        $"apt:{epoch}:l:role:{page}:{pageSize}:s{sortSeg}:{(desc ? 1 : 0)}";
} // Kết thúc EntityCacheKeys.

// Giao diện lưu/đọc JSON entity qua distributed cache — service gọi, tracker cập nhật hit/miss.
public interface IEntityResponseCache
{ // Mở khối IEntityResponseCache.
    Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default) where T : class; // Đọc DTO.
    Task SetJsonAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class; // Ghi DTO.
    Task RemoveAsync(string key, CancellationToken cancellationToken = default); // Xóa một khóa.
    Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default); // Xóa lô.
} // Kết thúc IEntityResponseCache.

// Triển khai: camelCase JSON, TTL clamp; lỗi cache → miss hoặc bỏ ghi (API vẫn trả DB).
public sealed class EntityResponseCache : IEntityResponseCache
{ // Mở khối EntityResponseCache.
    private static readonly JsonSerializerOptions Json = new()
    { // Tuỳ chọn JSON cố định cho cache — nhất quán với API.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // Khớp client thường dùng camelCase.
        WriteIndented = false // Gọn để tiết kiệm bộ nhớ Redis.
    };

    private readonly IDistributedCache _cache; // Redis hoặc memory bridge.
    private readonly CacheResponseTracker _tracker; // Báo cáo HIT/MISS cho filter.
    private readonly TimeSpan _ttl; // TTL thực tế sau clamp options.
    private readonly ILogger<EntityResponseCache> _log; // Debug khi đọc/ghi lỗi mềm.

    public EntityResponseCache(
        IDistributedCache cache,
        CacheResponseTracker tracker,
        IOptions<CacheOptions> options,
        ILogger<EntityResponseCache> log)
    { // Mở khối constructor EntityResponseCache.
        // BƯỚC 1 — Lưu dependency.
        _cache = cache; // Distributed cache đã failover nếu cấu hình.
        _tracker = tracker; // Per-request (scoped) tracker.
        _log = log; // Category EntityResponseCache.
        // BƯỚC 2 — Clamp TTL entity giữa 30s và 1 ngày.
        var sec = Math.Clamp(options.Value.EntityTtlSeconds, 30, 86_400);
        _ttl = TimeSpan.FromSeconds(sec);
    } // Kết thúc constructor EntityResponseCache.

    // Đọc chuỗi JSON → deserialize T; miss/lỗi → null và ReportLookup(false).
    public async Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    { // Mở khối GetJsonAsync.
        string? raw;
        try
        { // BƯỚC 1 — Lấy chuỗi từ distributed cache.
            raw = await _cache.GetStringAsync(key, cancellationToken);
        }
        catch (OperationCanceledException)
        { // Hủy — bubble.
            throw;
        }
        catch (Exception ex)
        { // BƯỚC 2 — Lỗi mạng/Redis: coi như miss.
            _log.LogDebug(ex, "GetStringAsync: error; treating as cache miss.");
            _tracker.ReportLookup(false);
            return null;
        }

        // TRƯỜNG HỢP A — Không có payload.
        if (string.IsNullOrEmpty(raw))
        {
            _tracker.ReportLookup(false);
            return null;
        }

        try
        { // BƯỚC 3 — Deserialize an toàn.
            var dto = JsonSerializer.Deserialize<T>(raw, Json);
            if (dto is null)
            { // JSON hợp lệ nhưng ra null reference type.
                _tracker.ReportLookup(false);
                return null;
            }

            _tracker.ReportLookup(true);
            return dto;
        }
        catch (JsonException)
        { // TRƯỜNG HỢP B — Payload corrupt: miss mềm.
            _tracker.ReportLookup(false);
            return null;
        }
    } // Kết thúc GetJsonAsync.

    // Serialize + SetString với AbsoluteExpirationRelativeToNow = TTL; lỗi ghi chỉ log.
    public async Task SetJsonAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class
    { // Mở khối SetJsonAsync.
        // BƯỚC 1 — Serialize một lần.
        var raw = JsonSerializer.Serialize(value, Json);
        try
        { // BƯỚC 2 — Ghi cache.
            await _cache.SetStringAsync(
                key,
                raw,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl },
                cancellationToken);
        }
        catch (Exception ex)
        { // BƯỚC 3 — Không throw — API vẫn trả 200 từ DB.
            _log.LogDebug(ex, "SetStringAsync: skipping cache write; API still returns data from the database.");
        }
    } // Kết thúc SetJsonAsync.

    // Remove một khóa — swallow exception (invalidation mềm).
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    { // Mở khối RemoveAsync.
        try
        { // BƯỚC 1 — Xóa khỏi distributed cache.
            await _cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex)
        { // BƯỚC 2 — Log và tiếp tục.
            _log.LogDebug(ex, "RemoveAsync: skipping removal of key {Key}.", key);
        }
    } // Kết thúc RemoveAsync.

    // Remove từng khóa — mỗi key try/catch riêng.
    public async Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    { // Mở khối RemoveManyAsync.
        foreach (var key in keys)
        { // BƯỚC 1 — Tuần tự RemoveAsync (đủ cho invalidation nhỏ).
            try
            {
                await _cache.RemoveAsync(key, cancellationToken);
            }
            catch (Exception ex)
            { // BƯỚC 2 — Một khóa lỗi không chặn các khóa sau.
                _log.LogDebug(ex, "RemoveAsync failed (key: {Key}); skipping.", key);
            }
        }
    } // Kết thúc RemoveManyAsync.
} // Kết thúc EntityResponseCache.

#endregion

#region Bọc prefix và failover Redis → memory

// Bọc IDistributedCache: thêm prefix instance (InstanceName) cho memory layer — cô lập khóa theo app.
public sealed class PrefixedMemoryDistributedCache : IDistributedCache
{ // Mở khối PrefixedMemoryDistributedCache.
    private readonly IDistributedCache _inner; // MemoryDistributedCache thật.
    private readonly string _prefix; // Ví dụ "ApartmentAPI:".

    public PrefixedMemoryDistributedCache(IDistributedCache inner, string instanceName)
    { // Mở khối constructor.
        _inner = inner; // MemoryDistributedCache singleton.
        _prefix = string.IsNullOrEmpty(instanceName) ? string.Empty : instanceName; // Tránh null prefix.
    } // Kết thúc constructor.

    private string P(string k) => _prefix + k; // Nối prefix + key gốc.

    public byte[]? Get(string key) => _inner.Get(P(key)); // Sync get.
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => _inner.GetAsync(P(key), token); // Async get.
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _inner.Set(P(key), value, options);
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
        _inner.SetAsync(P(key), value, options, token);
    public void Remove(string key) => _inner.Remove(P(key));
    public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(P(key), token);
    public void Refresh(string key) => _inner.Refresh(P(key));
    public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(P(key), token);
} // Kết thúc PrefixedMemoryDistributedCache.

// Decorator: ưu tiên Redis; khi lỗi kiểu kết nối — đọc/ghi/xóa trên memory có cùng ý nghĩa key (sau prefix).
public sealed class RedisFirstFailoverMemoryDistributedCache : IDistributedCache
{ // Mở khối RedisFirstFailoverMemoryDistributedCache.
    private readonly RedisCache _redis; // Provider chính.
    private readonly IDistributedCache _memoryPrefixed; // Fallback đã prefix.
    private readonly CacheBackendState _state; // Ghi nhận redis vs memory.
    private readonly ILogger<RedisFirstFailoverMemoryDistributedCache> _log; // Warning khi failover.

    public RedisFirstFailoverMemoryDistributedCache(
        RedisCache redis,
        PrefixedMemoryDistributedCache memoryPrefixed,
        CacheBackendState state,
        ILogger<RedisFirstFailoverMemoryDistributedCache> log)
    { // Mở khối constructor.
        _redis = redis; // RedisCache singleton.
        _memoryPrefixed = memoryPrefixed; // Shadow store.
        _state = state; // Cho descriptor/health.
        _log = log;
    } // Kết thúc constructor.

    public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult(); // Sync bridge — tránh dùng trong async path.

    // Đọc Redis; fail mạng → memory + Kind=memory.
    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    { // Mở khối GetAsync (failover).
        try
        { // BƯỚC 1 — Thử Redis trước.
            var b = await _redis.GetAsync(key, token);
            _state.Kind = "redis"; // Thành công — backend hiện tại Redis.
            return b;
        }
        catch (Exception ex) when (ShouldFailoverToMemory(ex))
        { // BƯỚC 2 — Failover sang memory.
            _log.LogWarning(ex, "Redis cache unavailable; reading from in-memory fallback (key: {Key}).", key);
            var m = await _memoryPrefixed.GetAsync(key, token);
            _state.Kind = "memory";
            return m;
        }
    } // Kết thúc GetAsync.

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
        SetAsync(key, value, options).GetAwaiter().GetResult();

    // Ghi Redis; fail → ghi memory.
    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    { // Mở khối SetAsync (failover).
        try
        { // BƯỚC 1 — Ghi Redis.
            await _redis.SetAsync(key, value, options, token);
            _state.Kind = "redis";
        }
        catch (Exception ex) when (ShouldFailoverToMemory(ex))
        { // BƯỚC 2 — Ghi bóng lên memory.
            _log.LogWarning(ex, "Redis cache unavailable; writing to in-memory fallback (key: {Key}).", key);
            await _memoryPrefixed.SetAsync(key, value, options, token);
            _state.Kind = "memory";
        }
    } // Kết thúc SetAsync.

    public void Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();

    // Xóa Redis (best effort) + luôn xóa memory để không stale shadow.
    public async Task RemoveAsync(string key, CancellationToken token = default)
    { // Mở khối RemoveAsync.
        var redisRemoved = false;
        try
        { // BƯỚC 1 — Remove Redis.
            await _redis.RemoveAsync(key, token);
            redisRemoved = true;
            _state.Kind = "redis";
        }
        catch (Exception ex) when (ShouldFailoverToMemory(ex))
        { // Redis lỗi — vẫn phải quét memory.
            _log.LogDebug(ex, "Redis Remove failed; clearing shadow key in in-memory fallback (key: {Key}).", key);
        }

        try
        { // BƯỚC 2 — Luôn remove memory (đồng bộ shadow).
            await _memoryPrefixed.RemoveAsync(key, token);
        }
        catch { /* bỏ qua lỗi memory nhỏ */ }

        // TRƯỜNG HỢP A — Redis không remove được → coi backend hiện tại là memory.
        if (!redisRemoved)
            _state.Kind = "memory";
    } // Kết thúc RemoveAsync.

    public void Refresh(string key) => RefreshAsync(key).GetAwaiter().GetResult();

    // Sliding expiration: Redis trước; fail → memory Refresh.
    public async Task RefreshAsync(string key, CancellationToken token = default)
    { // Mở khối RefreshAsync.
        try
        { // BƯỚC 1 — Refresh Redis.
            await _redis.RefreshAsync(key, token);
            _state.Kind = "redis";
        }
        catch (Exception ex) when (ShouldFailoverToMemory(ex))
        { // BƯỚC 2 — Refresh shadow.
            await _memoryPrefixed.RefreshAsync(key, token);
            _state.Kind = "memory";
        }
    } // Kết thúc RefreshAsync.

    // Điều kiện failover: lỗi mạng/Redis phổ biến — không failover trên mọi Exception.
    private static bool ShouldFailoverToMemory(Exception ex) =>
        ex is RedisException
        or System.IO.IOException
        or TimeoutException
        or System.Net.Sockets.SocketException;
} // Kết thúc RedisFirstFailoverMemoryDistributedCache.

#endregion

#region Đăng ký cache trong Program / builder

// Extension đăng ký distributed cache + singleton Redis stack + failover decorator.
public static class DistributedCaching
{ // Mở khối DistributedCaching.
    // Cấu hình CacheOptions, probe log, Redis hoặc memory-only theo connection string / PreferInProcessCache.
    public static void AddDistributedCaching(this WebApplicationBuilder builder)
    { // Mở khối AddDistributedCaching.
        // BƯỚC 1 — Bind section Cache từ IConfiguration.
        builder.Services.Configure<CacheOptions>(builder.Configuration.GetSection(CacheOptions.SectionName));
        var cacheOptions = builder.Configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>() ?? new();

        // BƯỚC 2 — Logger tạm để log quyết định backend trước khi host build xong.
        using var loggerFactory = CreateProbeLoggerFactory(builder);
        var log = loggerFactory.CreateLogger("ApartmentAPI.Cache");

        // BƯỚC 3 — State + descriptor cho inspection.
        builder.Services.AddSingleton<CacheBackendState>();
        builder.Services.AddSingleton<CacheBackendDescriptor>(sp =>
            new CacheBackendDescriptor(sp.GetRequiredService<CacheBackendState>()));

        // TRƯỜNG HỢP A — Dev hoặc yêu cầu chỉ memory: bỏ Redis hoàn toàn.
        if (cacheOptions.PreferInProcessCache)
        {
            log.LogInformation(
                "Cache: {Name}=true — using in-process memory only (DistributedMemory); Redis skipped.",
                nameof(CacheOptions.PreferInProcessCache));
            builder.Services.AddDistributedMemoryCache();
            return;
        }

        // BƯỚC 4 — Lấy connection string Redis.
        var rawCs = builder.Configuration.GetConnectionString("Redis");
        // TRƯỜNG HỢP B — Không cấu hình Redis → memory distributed.
        if (string.IsNullOrWhiteSpace(rawCs))
        {
            log.LogWarning("Cache: ConnectionStrings:Redis is not set — using in-process memory.");
            builder.Services.AddDistributedMemoryCache();
            return;
        }

        // BƯỚC 5 — Tránh block startup khi Redis down: abortConnect=false nếu thiếu.
        var connectionString = EnsureAbortConnectNotBlocking(rawCs);
        const string instanceName = "ApartmentAPI:"; // Prefix khóa Redis + memory shadow.

        // BƯỚC 6 — RedisCacheOptions + đăng ký RedisCache, MemoryDistributedCache, PrefixedMemory, IDistributedCache = failover.
        builder.Services.Configure<RedisCacheOptions>(o =>
        {
            o.Configuration = connectionString; // Chuỗi StackExchange.
            o.InstanceName = instanceName; // Prefix mọi key Redis.
        });
        builder.Services.AddSingleton<RedisCache>();
        builder.Services.AddSingleton<MemoryDistributedCache>(_ =>
            new MemoryDistributedCache(MSOptions.Create(new MemoryDistributedCacheOptions())));

        builder.Services.AddSingleton<PrefixedMemoryDistributedCache>(sp =>
            new PrefixedMemoryDistributedCache(sp.GetRequiredService<MemoryDistributedCache>(), instanceName));

        builder.Services.AddSingleton<IDistributedCache, RedisFirstFailoverMemoryDistributedCache>();

        log.LogInformation(
            "Cache: Redis preferred (InstanceName {Instance}); in-memory fallback when Redis is unavailable; no restart required to use Redis again when the service recovers.",
            instanceName);
    } // Kết thúc AddDistributedCaching.

    // Thêm abortConnect=false nếu chuỗi gốc chưa có — giảm treo khi Redis không lên.
    private static string EnsureAbortConnectNotBlocking(string connectionString)
    { // Mở khối EnsureAbortConnectNotBlocking.
        if (connectionString.Contains("abortConnect", StringComparison.OrdinalIgnoreCase))
            return connectionString; // Người dùng đã tự cấu hình.
        return connectionString.TrimEnd(' ', ';') + ",abortConnect=false"; // Gắn mặc định an toàn cho dev.
    } // Kết thúc EnsureAbortConnectNotBlocking.

    // Factory log tối giản đọc Logging:Console từ configuration.
    private static ILoggerFactory CreateProbeLoggerFactory(WebApplicationBuilder builder) =>
        LoggerFactory.Create(logging =>
        {
            logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
            logging.AddConsole();
        });
} // Kết thúc DistributedCaching.

// Descriptor chỉ đọc Kind từ state — tiêm nơi cần expose "redis" vs "memory".
public sealed class CacheBackendDescriptor
{ // Mở khối CacheBackendDescriptor.
    private readonly CacheBackendState _state; // Singleton được filter cache cập nhật.

    public CacheBackendDescriptor(CacheBackendState state) => _state = state; // Constructor expression.

    public string Kind => _state.Kind; // "redis" hoặc "memory" tùy lần gọi cache gần nhất thành công.
} // Kết thúc CacheBackendDescriptor.

#endregion
