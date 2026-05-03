using System.Security.Cryptography; 
using System.Text; 
using System.Text.Json; 
using Microsoft.AspNetCore.Builder; 
using Microsoft.Extensions.Caching.Distributed; 
using Microsoft.Extensions.Caching.Memory; 
using Microsoft.Extensions.Caching.StackExchangeRedis; 
using Microsoft.Extensions.Logging; 
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CommentAPI;

// =============================================================================
// File DistributedCaching.cs: đăng ký + cấu hình + triển khai cache phân tán (Redis ưu tiên,
// in-memory dự phòng) và lớp tiện ích IEntityResponseCache (JSON).
// =============================================================================

// Cấu hình cache entity (JSON) và cách chọn backend.
public sealed class CacheOptions // Bind từ appsettings section "Cache".
{
    public const string SectionName = "Cache"; // Tên section cấu hình.

    // Thời gian sống mặc định của mỗi key entity (giây).
    public int EntityTtlSeconds { get; set; } = 120; // Mặc định 120s.

    // Khi true (tùy chọn dev): ép chỉ dùng bộ nhớ trong process, không dùng Redis.
    // Mặc định false = luôn ưu tiên Redis nếu có connection string Redis.
    public bool PreferInProcessCache { get; set; } = false; // Tắt Redis khi true.

    // Giới hạn thời gian (ms) cho thao tác phụ; để dự phòng tương lai/điều chỉnh chuỗi kết nối (500..30000).
    public int RedisProbeTimeoutMilliseconds { get; set; } = 5_000; // Hiện chưa dùng probe blocking.
}

// Trạng thái backend dùng cho header — cập nhật theo từng thao tác cache thành công (redis / memory).
public sealed class CacheBackendState // Singleton mutable kind string.
{
    private string _kind = "memory"; // Default until Redis succeeds.

    // Header X-Cache-Backend: redis hoặc memory (cái mới dùng gần nhất cho request hiện tại nếu có nhiều bước).
    public string Kind // Backend label.
    {
        get => _kind; // Read current.
        set => _kind = value; // Write last successful backend.
    }
}

// Scoped: HIT/MISS theo request cho header X-Cache-Status.
public sealed class CacheResponseTracker // Per-request cache lookup telemetry.
{
    public bool LookupPerformed { get; private set; } // Có gọi Get không.
    public bool WasHit { get; private set; } // Kết quả hit nếu đã lookup.

    public void ReportLookup(bool cacheHit) // EntityResponseCache gọi sau GetJsonAsync.
    {
        LookupPerformed = true; // Mark touched.
        WasHit = cacheHit; // Hit flag.
    }
}

public static class EntityCacheHash // Hash tiền tố cho term dài trong key.
{
    public static string SearchTerm(string normalizedTerm) // SHA256 truncate hex.
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedTerm)); // Hash UTF-8 bytes.
        return Convert.ToHexString(bytes.AsSpan(0, 8)); // 8 byte đầu → hex ngắn.
    }
}

public static class EntityCacheKeys // Factory khóa string thống nhất.
{
    public static string User(Guid id) => $"u:{id:N}"; // Chi tiết user.
    public static string Post(Guid id) => $"p:{id:N}"; // Chi tiết post.
    public static string Comment(Guid id) => $"c:{id:N}"; // Chi tiết comment.

    public static string UsersPaged(int page, int pageSize) => $"l:users:{page}:{pageSize}"; // List users (không filter).

    public static string PostsPaged(int page, int pageSize) => $"l:posts:{page}:{pageSize}"; // List posts (không filter).

    public static string CommentsAll(int page, int pageSize) => $"l:comments:all:{page}:{pageSize}"; // All comments page.
    public static string CommentsFlatAll(int page, int pageSize) => $"l:comments:flat:all:{page}:{pageSize}"; // Flat route page.

    public static string CommentsByUser(Guid userId, int page, int pageSize) => // Comment theo tác giả (UserId).
        $"l:comments:u:{userId:N}:{page}:{pageSize}"; // Khóa cache list-by-user.
    public static string CommentsSearchContent(string termHash, int page, int pageSize) => $"l:comments:sc:{termHash}:{page}:{pageSize}"; // Search content.

    public static string CommentsSearchContentInPost(Guid postId, string termHash, int page, int pageSize) => // Scoped search.
        $"l:comments:p:{postId:N}:sc:{termHash}:{page}:{pageSize}"; // Key string.
    public static string CommentsAllTreeFlat(int page, int pageSize) => $"l:comments:tree:flat:{page}:{pageSize}"; // Tree flat global (EF).
    public static string CommentsAllTreeCte(int page, int pageSize) => $"l:comments:tree:cte:{page}:{pageSize}"; // Tree global từ hàng CTE + dựng cây.
    public static string CommentsAllFlattenEfTree(int page, int pageSize) => $"l:comments:flat:eftree:{page}:{pageSize}"; // EF flatten.
    public static string CommentsAllFlattenCteTree(int page, int pageSize) => $"l:comments:flat:ctetree:{page}:{pageSize}"; // CTE flatten.
    public static string CommentsAllCteFlat(int page, int pageSize) => $"l:comments:cteflat:{page}:{pageSize}"; // CTE flat list.

    public static string CommentsFlatByPost(Guid postId, int page, int pageSize) => // Per post flat.
        $"l:comments:p:{postId:N}:flat:{page}:{pageSize}"; // Key.

    public static string CommentsCteFlatByPost(Guid postId, int page, int pageSize) => // Per post CTE flat.
        $"l:comments:p:{postId:N}:cteflat:{page}:{pageSize}"; // Key.

    public static string CommentsTreeByPost(Guid postId, int page, int pageSize) => // Per post tree (EF).
        $"l:comments:p:{postId:N}:tree:{page}:{pageSize}"; // Key.

    public static string CommentsTreeCteByPost(Guid postId, int page, int pageSize) => // Per post tree từ CTE.
        $"l:comments:p:{postId:N}:tree:cte:{page}:{pageSize}"; // Key tách biệt khỏi EF tree.

    public static string CommentsFlattenedCteTree(Guid postId, int page, int pageSize) => // Per post CTE tree flatten.
        $"l:comments:p:{postId:N}:ctetree:{page}:{pageSize}"; // Key.

    public static string CommentsFlattenedEfTreeByPost(Guid postId, int page, int pageSize) => // Per post EF flatten.
        $"l:comments:p:{postId:N}:eftreeflat:{page}:{pageSize}"; // Key.
}

public interface IEntityResponseCache // Interface JSON cache cho DTO phản hồi.
{
    Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default) where T : class; // Deserialize miss → null.
    Task SetJsonAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class; // Set TTL.
    Task RemoveAsync(string key, CancellationToken cancellationToken = default); // Invalidate một key.
    Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default); // Invalidate lô.
}

public sealed class EntityResponseCache : IEntityResponseCache // Triển khai trên IDistributedCache.
{
    private static readonly JsonSerializerOptions Json = new() // Tùy chọn JSON cố định.
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // camelCase output.
        WriteIndented = false // Compact.
    };

    private readonly IDistributedCache _cache; // Backend thực tế (Redis-first hoặc memory).
    private readonly CacheResponseTracker _tracker; // Báo cáo HIT/MISS.
    private readonly TimeSpan _ttl; // TTL clamped từ options.
    private readonly ILogger<EntityResponseCache> _log; // Logger soft-fail.

    public EntityResponseCache( // DI ctor.
        IDistributedCache cache, // Distributed cache.
        CacheResponseTracker tracker, // Scoped tracker.
        IOptions<CacheOptions> options, // Config.
        ILogger<EntityResponseCache> log) // Logger.
    {
        _cache = cache; // Assign cache.
        _tracker = tracker; // Assign tracker.
        _log = log; // Assign log.
        var sec = Math.Clamp(options.Value.EntityTtlSeconds, 30, 86_400); // Clamp 30s..1d.
        _ttl = TimeSpan.FromSeconds(sec); // Timespan TTL.
    }

    public async Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default) where T : class // Read JSON.
    {
        string? raw; // Raw string from cache.
        try // Isolate cache failures.
        {
            raw = await _cache.GetStringAsync(key, cancellationToken); // Network/memory get.
        }
        catch (OperationCanceledException) // Propagate cancel.
        {
            throw; // Rethrow.
        }
        catch (Exception ex) // Any other — treat as miss.
        {
            _log.LogDebug(ex, "GetStringAsync: lỗi; coi như miss."); // Debug only.
            _tracker.ReportLookup(false); // Miss.
            return null; // Degrade.
        }

        if (string.IsNullOrEmpty(raw)) // Empty key or missing.
        {
            _tracker.ReportLookup(false); // Miss.
            return null; // Null DTO.
        }

        try // Deserialize guard.
        {
            var dto = JsonSerializer.Deserialize<T>(raw, Json); // Parse JSON.
            if (dto is null) // Serializer returned null for reference type.
            {
                _tracker.ReportLookup(false); // Treat as miss.
                return null; // Null.
            }

            _tracker.ReportLookup(true); // Hit.
            return dto; // DTO.
        }
        catch (JsonException) // Bad payload in cache.
        {
            _tracker.ReportLookup(false); // Miss.
            return null; // Ignore corrupt.
        }
    }

    public async Task SetJsonAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class // Write JSON.
    {
        var raw = JsonSerializer.Serialize(value, Json); // Serialize.
        try // Set may fail silently for availability.
        {
            await _cache.SetStringAsync( // Put with TTL.
                key, // Key.
                raw, // JSON string.
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl }, // Relative expiry.
                cancellationToken); // CT.
        }
        catch (Exception ex) // Log and continue — API still returns DB result.
        {
            _log.LogDebug(ex, "SetStringAsync: bỏ qua ghi cache; API vẫn trả dữ liệu từ DB."); // Soft fail.
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default) // Remove one.
    {
        try // Remove best-effort.
        {
            await _cache.RemoveAsync(key, cancellationToken); // Delete key.
        }
        catch (Exception ex) // Log only.
        {
            _log.LogDebug(ex, "RemoveAsync: bỏ qua xóa key {Key}.", key); // Debug.
        }
    }

    public async Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) // Remove many.
    {
        foreach (var key in keys) // Sequential removes.
        {
            try // Per key.
            {
                await _cache.RemoveAsync(key, cancellationToken); // Remove.
            }
            catch (Exception ex) // Continue on error.
            {
                _log.LogDebug(ex, "RemoveAsync lỗi (key: {Key}); bỏ qua.", key); // Debug.
            }
        }
    }
}

// Ghi nhớ tương tự RedisCache: tiền tố RedisCacheOptions.InstanceName trên từng key,
// để bộ nhớ dự phòng cùng không gian tên với key Redis thực tế.
public sealed class PrefixedMemoryDistributedCache : IDistributedCache // Wrapper thêm instance prefix.
{
    private readonly IDistributedCache _inner; // MemoryDistributedCache inner.
    private readonly string _prefix; // InstanceName + optional colon.

    public PrefixedMemoryDistributedCache(IDistributedCache inner, string instanceName) // Ctor.
    {
        _inner = inner; // Inner cache.
        _prefix = string.IsNullOrEmpty(instanceName) ? string.Empty : instanceName; // Prefix string.
    }

    private string P(string k) => _prefix + k; // Apply prefix.

    public byte[]? Get(string key) => _inner.Get(P(key)); // Sync get.
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => _inner.GetAsync(P(key), token); // Async get.
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _inner.Set(P(key), value, options); // Sync set.
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => // Async set.
        _inner.SetAsync(P(key), value, options, token); // Forward.
    public void Remove(string key) => _inner.Remove(P(key)); // Sync remove.
    public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(P(key), token); // Async remove.
    public void Refresh(string key) => _inner.Refresh(P(key)); // Sync refresh.
    public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(P(key), token); // Async refresh.
}

// Luôn thử RedisCache trước; lỗi mạng / Redis down → cùng key logic qua
// bộ nhớ dự phòng. Khi Redis sống lại, lần thao tác thành công tới sẽ lại dùng Redis
// (không cần restart API).
public sealed class RedisFirstFailoverMemoryDistributedCache : IDistributedCache // Composite failover.
{
    private readonly RedisCache _redis; // Primary Redis implementation.
    private readonly IDistributedCache _memoryPrefixed; // Fallback with same logical key space.
    private readonly CacheBackendState _state; // Update Kind for headers.
    private readonly ILogger<RedisFirstFailoverMemoryDistributedCache> _log; // Warn on failover.

    public RedisFirstFailoverMemoryDistributedCache( // Ctor.
        RedisCache redis, // Redis.
        PrefixedMemoryDistributedCache memoryPrefixed, // Prefixed memory.
        CacheBackendState state, // State.
        ILogger<RedisFirstFailoverMemoryDistributedCache> log) // Logger.
    {
        _redis = redis; // Assign.
        _memoryPrefixed = memoryPrefixed; // Assign.
        _state = state; // Assign.
        _log = log; // Assign.
    }

    public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult(); // Sync over async (avoid deadlocks in sync callers).

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default) // Async get with failover.
    {
        try // Redis first.
        {
            var b = await _redis.GetAsync(key, token); // Try Redis.
            _state.Kind = "redis"; // Mark backend.
            return b; // Bytes or null.
        }
        catch (Exception ex) when (ShouldFailoverToMemory(ex)) // Transient Redis errors.
        {
            _log.LogWarning(ex, "Cache Redis hỏng; đọc bộ nhớ dự phòng (key: {Key}).", key); // Ops visibility.
            var m = await _memoryPrefixed.GetAsync(key, token); // Fallback read.
            _state.Kind = "memory"; // Mark memory path.
            return m; // Bytes or null.
        }
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => // Sync set.
        SetAsync(key, value, options).GetAwaiter().GetResult(); // Block on async.

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) // Async set.
    {
        try // Redis first.
        {
            await _redis.SetAsync(key, value, options, token); // Write Redis.
            _state.Kind = "redis"; // Mark.
        }
        catch (Exception ex) when (ShouldFailoverToMemory(ex)) // Failover.
        {
            _log.LogWarning(ex, "Cache Redis hỏng; ghi bộ nhớ dự phòng (key: {Key}).", key); // Warn.
            await _memoryPrefixed.SetAsync(key, value, options, token); // Write memory.
            _state.Kind = "memory"; // Mark.
        }
    }

    public void Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult(); // Sync remove.

    public async Task RemoveAsync(string key, CancellationToken token = default) // Remove both layers best-effort.
    {
        var redisRemoved = false; // Track Redis success.
        try // Try Redis.
        {
            await _redis.RemoveAsync(key, token); // Redis delete.
            redisRemoved = true; // Flag.
            _state.Kind = "redis"; // Mark.
        }
        catch (Exception ex) when (ShouldFailoverToMemory(ex)) // Redis down.
        {
            _log.LogDebug(ex, "Redis Remove lỗi; sẽ gỡ bóng ở bộ nhớ dự phòng (key: {Key}).", key); // Debug.
        }

        try // Always try shadow memory.
        {
            await _memoryPrefixed.RemoveAsync(key, token); // Memory delete.
        }
        catch // Swallow — shadow cleanup best effort.
        {
            /* bỏ qua: xóa bóng */ // Intentionally empty.
        }

        if (!redisRemoved) // If Redis path failed earlier.
        {
            _state.Kind = "memory"; // Report memory as active path.
        }
    }

    public void Refresh(string key) => RefreshAsync(key).GetAwaiter().GetResult(); // Sync refresh.

    public async Task RefreshAsync(string key, CancellationToken token = default) // Refresh sliding semantics.
    {
        try // Redis refresh.
        {
            await _redis.RefreshAsync(key, token); // Primary.
            _state.Kind = "redis"; // Mark.
        }
        catch (Exception ex) when (ShouldFailoverToMemory(ex)) // Failover.
        {
            await _memoryPrefixed.RefreshAsync(key, token); // Memory refresh.
            _state.Kind = "memory"; // Mark.
        }
    }

    // Lỗi tầng kết nối Redis / thời gian chờ: chuyển sang memory (cùng cấp với ứng dụng, không tốn DB).
    private static bool ShouldFailoverToMemory(Exception ex) => // Predicate for catch when.
        ex is RedisException // StackExchange Redis errors.
        or System.IO.IOException // IO broken pipe etc.
        or TimeoutException // Timeouts.
        or System.Net.Sockets.SocketException; // TCP failures.
}

// Đăng ký IDistributedCache: ưu tiên Redis, dự phòng bộ nhớ; chỉ bộ nhớ nếu thiếu cấu hình / ép cờ dev.
public static class DistributedCaching // Extension AddDistributedCaching.
{
    public static void AddDistributedCaching(this WebApplicationBuilder builder) // Host builder entry.
    {
        builder.Services.Configure<CacheOptions>(builder.Configuration.GetSection(CacheOptions.SectionName)); // Options pattern.
        var cacheOptions = builder.Configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>() ?? new(); // Snapshot read.

        using var loggerFactory = CreateProbeLoggerFactory(builder); // Short-lived factory for startup logs.
        var log = loggerFactory.CreateLogger("CommentAPI.Cache"); // Category logger.
        builder.Services.AddSingleton<CacheBackendState>(); // Singleton state for Kind.
        // Gắn header từ cùng singleton trạng thái
        builder.Services.AddSingleton<CacheBackendDescriptor>(sp => new CacheBackendDescriptor(sp.GetRequiredService<CacheBackendState>())); // Adapter.

        if (cacheOptions.PreferInProcessCache) // Dev force memory.
        {
            log.LogInformation( // Info.
                "Cache: {Name}=true — chỉ dùng bộ nhớ (DistributedMemory), bỏ qua Redis.", // Message template.
                nameof(CacheOptions.PreferInProcessCache)); // Property name.
            builder.Services.AddDistributedMemoryCache(); // Pure memory distributed cache.
            return; // Skip Redis registration.
        }

        var rawCs = builder.Configuration.GetConnectionString("Redis"); // Connection string optional.
        if (string.IsNullOrWhiteSpace(rawCs)) // Missing Redis CS.
        {
            log.LogWarning("Cache: không có ConnectionStrings:Redis — dùng bộ nhớ trong process."); // Warn.
            builder.Services.AddDistributedMemoryCache(); // Fallback only memory.
            return; // Done.
        }

        var connectionString = EnsureAbortConnectNotBlocking(rawCs); // Append abortConnect=false if absent.
        const string instanceName = "CommentAPI:"; // Logical isolation prefix.

        builder.Services.Configure<RedisCacheOptions>(o => // Configure Redis client options.
        {
            o.Configuration = connectionString; // Server connection string.
            o.InstanceName = instanceName; // Key prefix for RedisCache.
        });
        // Redis thật: kết nối lười / thử lại; không cần probe tại startup (tránh cảnh tụ trước: luôn rơi về memory).
        builder.Services.AddSingleton<RedisCache>(); // Concrete Redis cache singleton.
        builder.Services.AddSingleton<MemoryDistributedCache>(sp => new MemoryDistributedCache( // In-memory distributed impl.
            Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()))); // Default options.

        builder.Services.AddSingleton<PrefixedMemoryDistributedCache>(sp => // Prefixed wrapper registration.
            new PrefixedMemoryDistributedCache( // New instance.
                sp.GetRequiredService<MemoryDistributedCache>(), // Inner memory.
                instanceName)); // Same prefix as Redis.

        builder.Services.AddSingleton<IDistributedCache, RedisFirstFailoverMemoryDistributedCache>(); // Composite as IDistributedCache.

        log.LogInformation( // Success path log.
            "Cache: Redis ưu tiên (InstanceName {Instance}), bộ nhớ dự phòng nếu Redis không phản hồi; không cần restart để dùng lại Redis khi dịch vụ sống lại.", // Message.
            instanceName); // Argument.
    }

    private static string EnsureAbortConnectNotBlocking(string connectionString) // Avoid blocking startup on Redis down.
    {
        if (connectionString.Contains("abortConnect", StringComparison.OrdinalIgnoreCase)) // Already specified.
            return connectionString; // As-is.
        return connectionString.TrimEnd(' ', ';') + ",abortConnect=false"; // Append best practice for ASP.NET.
    }

    private static ILoggerFactory CreateProbeLoggerFactory(WebApplicationBuilder builder) => // Local factory for registration logs.
        LoggerFactory.Create(logging => // Builder lambda.
        {
            logging.AddConfiguration(builder.Configuration.GetSection("Logging")); // Bind logging config.
            logging.AddConsole(); // Console sink for startup messages.
        });
}

// Adapter: giữ tên tương thích middleware cũ (chỉ đọc Kind).
public sealed class CacheBackendDescriptor // Thin read-only view.
{
    private readonly CacheBackendState _state; // Backing state.

    public CacheBackendDescriptor(CacheBackendState state) => _state = state; // Ctor expression.
    public string Kind => _state.Kind; // Expose Kind for headers.
}
