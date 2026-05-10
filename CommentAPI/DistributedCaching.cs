using System.Text.Json; // JsonSerializer: tuỳ chọn camelCase, serialize DTO cache.
using CommentAPI.Repositories; // CommentRepository.EnumerateCommentCteSortSpecsForCache — sinh mọi khóa cache CTE theo sort.
using Microsoft.AspNetCore.Builder; // WebApplicationBuilder.AddDistributedCaching.
using Microsoft.Extensions.Caching.Distributed; // IDistributedCache, DistributedCacheEntryOptions, Get/Set string & byte.
using Microsoft.Extensions.Caching.Memory; // MemoryDistributedCache — lớp fallback trong process.
using Microsoft.Extensions.Caching.StackExchangeRedis; // RedisCache, RedisCacheOptions.
using Microsoft.Extensions.Logging; // ILogger, ILoggerFactory — log startup và soft-fail cache.
using Microsoft.Extensions.Options; // IOptions<CacheOptions>, Options.Create.
using StackExchange.Redis; // RedisException từ StackExchange khi failover sang bộ nhớ.

namespace CommentAPI;

// =============================================================================
// File DistributedCaching.cs: đăng ký IDistributedCache (Redis ưu tiên, memory dự phòng),
// CacheOptions, EntityCacheKeys, IEntityResponseCache/EntityResponseCache, wrapper prefix,
// failover Redis→memory, extension AddDistributedCaching.
// =============================================================================

// Cấu hình bind từ section "Cache" trong appsettings — TTL entity, cờ ép memory, timeout dự phòng.
public sealed class CacheOptions
{
    public const string SectionName = "Cache"; // Tên section trong IConfiguration.

    // Thời gian sống mặc định (giây) cho mỗi key JSON entity do EntityResponseCache ghi.
    public int EntityTtlSeconds { get; set; } = 120; // Mặc định 120 giây.

    // true: bỏ Redis, chỉ dùng DistributedMemoryCache (tiện máy dev không có Redis).
    public bool PreferInProcessCache { get; set; } = false;

    // Giới hạn thời gian probe (ms) — dự phòng mở rộng; hiện không chặn khởi động bằng probe blocking.
    public int RedisProbeTimeoutMilliseconds { get; set; } = 5_000;
} // Kết thúc CacheOptions.

// Singleton: nhãn backend gần nhất thành công — middleware gắn header X-Cache-Backend (redis | memory).
public sealed class CacheBackendState
{
    private string _kind = "memory"; // Mặc định cho tới khi Redis trả OK.

    public string Kind
    {
        get => _kind;
        set => _kind = value;
    }
} // Kết thúc CacheBackendState.

// Scoped theo HTTP request: ghi nhận đã lookup cache và có HIT không — header X-Cache-Status.
public sealed class CacheResponseTracker
{
    public bool LookupPerformed { get; private set; } // Đã gọi GetJsonAsync hay chưa.
    public bool WasHit { get; private set; } // Kết quả sau lookup.

    public void ReportLookup(bool cacheHit)
    {
        LookupPerformed = true;
        WasHit = cacheHit;
    }
} // Kết thúc CacheResponseTracker.

// Factory khóa chuỗi thống nhất — prefix phân biệt u:/p:/c:/l:/cmt:/pst:/usr: và tham số sort/epoch/post.
public static class EntityCacheKeys
{
    public static string User(Guid id) => $"u:{id:N}"; // Chi tiết user theo Guid.
    public static string Post(Guid id) => $"p:{id:N}"; // Chi tiết post.
    public static string Comment(Guid id) => $"c:{id:N}"; // Chi tiết comment.

    public static string PostsResourceCommentsTreeCte(Guid postId, bool includeReplies, SortByColumn sort) =>
        $"l:posts:{postId:N}:comments:tree:cte:ir{(includeReplies ? 1 : 0)}:s{sort.CacheKeySegment}"; // GET …/posts/{id}/comments/tree — cache rừng CTE.

    public static string PostsResourceCommentsFlatCte(Guid postId, bool includeReplies, SortByColumn sort) =>
        $"l:posts:{postId:N}:comments:flat:cte:ir{(includeReplies ? 1 : 0)}:s{sort.CacheKeySegment}"; // GET …/posts/{id}/comments/flat — danh sách phẳng CTE.

    public static IEnumerable<string> PostsResourceCommentsCteAllKeys(Guid postId)
    {
        foreach (var includeReplies in new[] { false, true })
        {
            foreach (var spec in CommentRepository.EnumerateCommentCteSortSpecsForCache())
            {
                yield return PostsResourceCommentsTreeCte(postId, includeReplies, spec);
                yield return PostsResourceCommentsFlatCte(postId, includeReplies, spec);
            }
        }
    } // Kết thúc PostsResourceCommentsCteAllKeys.

    public static string UsersPaged(long usersListEpoch, int page, int pageSize, SortByColumn sort) =>
        $"usr:{usersListEpoch}:l:users:{page}:{pageSize}:s{sort.CacheKeySegment}"; // Danh sách user phân trang + epoch.

    public static string PostsPaged(long postsListEpoch, int page, int pageSize, SortByColumn sort) =>
        $"pst:{postsListEpoch}:l:posts:{page}:{pageSize}:s{sort.CacheKeySegment}"; // Danh sách post phân trang + epoch.

    public static string CommentsFlatAll(long commentsListEpoch, int page, int pageSize, SortByColumn sort) =>
        $"cmt:{commentsListEpoch}:l:comments:flat:all:{page}:{pageSize}:s{sort.CacheKeySegment}"; // GET /comments/flat toàn hệ.

    public static string CommentsByUser(long commentsListEpoch, Guid userId, int page, int pageSize, SortByColumn sort) =>
        $"cmt:{commentsListEpoch}:l:comments:u:{userId:N}:{page}:{pageSize}:s{sort.CacheKeySegment}"; // Comment theo tác giả.

    public static string CommentsAllTreeFlat(long commentsListEpoch, int page, int pageSize, SortByColumn sort) =>
        $"cmt:{commentsListEpoch}:l:comments:tree:flat:{page}:{pageSize}:s{sort.CacheKeySegment}"; // Tree/flat phân trang.

    public static string CommentsAllTreeCte(long commentsListEpoch, int page, int pageSize, SortByColumn sort) =>
        $"cmt:{commentsListEpoch}:l:comments:tree:cte:{page}:{pageSize}:s{sort.CacheKeySegment}"; // Tree CTE phân trang.

    public static string CommentsAllTreeFlatFlatten(long commentsListEpoch, int page, int pageSize, SortByColumn sort) =>
        $"cmt:{commentsListEpoch}:l:comments:tree:flat:flatten:all:{page}:{pageSize}:s{sort.CacheKeySegment}"; // Flatten sau tree/flat.

    public static string CommentsAllFlattenCteTree(long commentsListEpoch, int page, int pageSize, SortByColumn sort) =>
        $"cmt:{commentsListEpoch}:l:comments:flat:ctetree:{page}:{pageSize}:s{sort.CacheKeySegment}"; // Cte tree từ flat.

    public static string CommentsAllCteFlat(long commentsListEpoch, int page, int pageSize, SortByColumn sort) =>
        $"cmt:{commentsListEpoch}:l:comments:cteflat:{page}:{pageSize}:s{sort.CacheKeySegment}"; // CTE phẳng toàn hệ.

    public static string CommentsFlatByPost(long commentsListEpoch, Guid postId, int page, int pageSize, SortByColumn sort) =>
        $"cmt:{commentsListEpoch}:l:comments:p:{postId:N}:flat:{page}:{pageSize}:s{sort.CacheKeySegment}"; // Flat theo post.

    public static string CommentsCteFlatByPost(long commentsListEpoch, Guid postId, int page, int pageSize, SortByColumn sort) =>
        $"cmt:{commentsListEpoch}:l:comments:p:{postId:N}:cteflat:{page}:{pageSize}:s{sort.CacheKeySegment}"; // CTE flat theo post.

    public static string CommentsTreeFlatByPost(long commentsListEpoch, Guid postId, int page, int pageSize, SortByColumn sort) =>
        $"cmt:{commentsListEpoch}:l:comments:p:{postId:N}:tree-flat:{page}:{pageSize}:s{sort.CacheKeySegment}"; // Tree flat theo post.

    public static string CommentsTreeCteByPost(long commentsListEpoch, Guid postId, int page, int pageSize, SortByColumn sort) =>
        $"cmt:{commentsListEpoch}:l:comments:p:{postId:N}:tree:cte:{page}:{pageSize}:s{sort.CacheKeySegment}"; // Tree CTE theo post.

    public static string CommentsFlattenedCteTree(long commentsListEpoch, Guid postId, int page, int pageSize, SortByColumn sort) =>
        $"cmt:{commentsListEpoch}:l:comments:p:{postId:N}:ctetree:{page}:{pageSize}:s{sort.CacheKeySegment}"; // Flatten CTE tree theo post.

    public static string CommentsTreeFlatFlattenByPost(long commentsListEpoch, Guid postId, int page, int pageSize, SortByColumn sort) =>
        $"cmt:{commentsListEpoch}:l:comments:p:{postId:N}:tree:flat:flatten:{page}:{pageSize}:s{sort.CacheKeySegment}"; // Flatten tree/flat theo post.
} // Kết thúc EntityCacheKeys.

public interface IEntityResponseCache // Hợp đồng cache JSON response (DTO/list) trên IDistributedCache.
{
    Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
    Task SetJsonAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class;
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
} // Kết thúc IEntityResponseCache.

public sealed class EntityResponseCache : IEntityResponseCache
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly IDistributedCache _cache;
    private readonly CacheResponseTracker _tracker;
    private readonly TimeSpan _ttl;
    private readonly ILogger<EntityResponseCache> _log;

    // BƯỚC 1 — Clamp TTL từ CacheOptions vào [30s, 1 ngày]; gán tracker + logger.
    public EntityResponseCache(
        IDistributedCache cache,
        CacheResponseTracker tracker,
        IOptions<CacheOptions> options,
        ILogger<EntityResponseCache> log)
    {
        _cache = cache;
        _tracker = tracker;
        _log = log;
        var sec = Math.Clamp(options.Value.EntityTtlSeconds, 30, 86_400);
        _ttl = TimeSpan.FromSeconds(sec);
    } // Kết thúc constructor.

    public async Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        string? raw;
        try
        {
            raw = await _cache.GetStringAsync(key, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "GetStringAsync: lỗi; coi như cache miss.");
            _tracker.ReportLookup(false);
            return null;
        }

        if (string.IsNullOrEmpty(raw))
        {
            _tracker.ReportLookup(false);
            return null;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<T>(raw, Json);
            if (dto is null)
            {
                _tracker.ReportLookup(false);
                return null;
            }

            _tracker.ReportLookup(true);
            return dto;
        }
        catch (JsonException)
        {
            _tracker.ReportLookup(false);
            return null;
        }
    } // Kết thúc GetJsonAsync.

    public async Task SetJsonAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class
    {
        var raw = JsonSerializer.Serialize(value, Json);
        try
        {
            await _cache.SetStringAsync(
                key,
                raw,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "SetStringAsync: bỏ qua ghi cache; API vẫn trả dữ liệu từ DB.");
        }
    } // Kết thúc SetJsonAsync.

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "RemoveAsync: bỏ qua xóa key {Key}.", key);
        }
    } // Kết thúc RemoveAsync.

    public async Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        foreach (var key in keys)
        {
            try
            {
                await _cache.RemoveAsync(key, cancellationToken);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "RemoveAsync thất bại (key: {Key}); bỏ qua.", key);
            }
        }
    } // Kết thúc RemoveManyAsync.
} // Kết thúc EntityResponseCache.

// Bọc MemoryDistributedCache: thêm InstanceName như RedisCache để cùng không gian tên logic với Redis.
public sealed class PrefixedMemoryDistributedCache : IDistributedCache
{
    private readonly IDistributedCache _inner;
    private readonly string _prefix;

    public PrefixedMemoryDistributedCache(IDistributedCache inner, string instanceName)
    {
        _inner = inner;
        _prefix = string.IsNullOrEmpty(instanceName) ? string.Empty : instanceName;
    }

    private string P(string k) => _prefix + k;

    public byte[]? Get(string key) => _inner.Get(P(key));
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => _inner.GetAsync(P(key), token);
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _inner.Set(P(key), value, options);
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
        _inner.SetAsync(P(key), value, options, token);
    public void Remove(string key) => _inner.Remove(P(key));
    public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(P(key), token);
    public void Refresh(string key) => _inner.Refresh(P(key));
    public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(P(key), token);
} // Kết thúc PrefixedMemoryDistributedCache.

// Đọc/ghi Redis trước; lỗi mạng/Redis → cùng key trên memory có prefix — không cần restart khi Redis hồi phục.
public sealed class RedisFirstFailoverMemoryDistributedCache : IDistributedCache
{
    private readonly RedisCache _redis;
    private readonly IDistributedCache _memoryPrefixed;
    private readonly CacheBackendState _state;
    private readonly ILogger<RedisFirstFailoverMemoryDistributedCache> _log;

    public RedisFirstFailoverMemoryDistributedCache(
        RedisCache redis,
        PrefixedMemoryDistributedCache memoryPrefixed,
        CacheBackendState state,
        ILogger<RedisFirstFailoverMemoryDistributedCache> log)
    {
        _redis = redis;
        _memoryPrefixed = memoryPrefixed;
        _state = state;
        _log = log;
    }

    public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult();

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        try
        {
            var b = await _redis.GetAsync(key, token);
            _state.Kind = "redis";
            return b;
        }
        catch (Exception ex) when (ShouldFailoverToMemory(ex))
        {
            _log.LogWarning(ex, "Redis không dùng được; đọc fallback memory (key: {Key}).", key);
            var m = await _memoryPrefixed.GetAsync(key, token);
            _state.Kind = "memory";
            return m;
        }
    } // Kết thúc GetAsync.

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
        SetAsync(key, value, options).GetAwaiter().GetResult();

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        try
        {
            await _redis.SetAsync(key, value, options, token);
            _state.Kind = "redis";
        }
        catch (Exception ex) when (ShouldFailoverToMemory(ex))
        {
            _log.LogWarning(ex, "Redis không ghi được; ghi fallback memory (key: {Key}).", key);
            await _memoryPrefixed.SetAsync(key, value, options, token);
            _state.Kind = "memory";
        }
    } // Kết thúc SetAsync.

    public void Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        var redisRemoved = false;
        try
        {
            await _redis.RemoveAsync(key, token);
            redisRemoved = true;
            _state.Kind = "redis";
        }
        catch (Exception ex) when (ShouldFailoverToMemory(ex))
        {
            _log.LogDebug(ex, "Redis Remove thất bại; xóa khóa bóng memory (key: {Key}).", key);
        }

        try
        {
            await _memoryPrefixed.RemoveAsync(key, token);
        }
        catch
        {
            /* bỏ qua — xóa bóng tốt nhất có thể */
        }

        if (!redisRemoved)
            _state.Kind = "memory";
    } // Kết thúc RemoveAsync.

    public void Refresh(string key) => RefreshAsync(key).GetAwaiter().GetResult();

    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        try
        {
            await _redis.RefreshAsync(key, token);
            _state.Kind = "redis";
        }
        catch (Exception ex) when (ShouldFailoverToMemory(ex))
        {
            await _memoryPrefixed.RefreshAsync(key, token);
            _state.Kind = "memory";
        }
    } // Kết thúc RefreshAsync.

    private static bool ShouldFailoverToMemory(Exception ex) =>
        ex is RedisException
        or System.IO.IOException
        or TimeoutException
        or System.Net.Sockets.SocketException;
} // Kết thúc RedisFirstFailoverMemoryDistributedCache.

public static class DistributedCaching
{
    // BƯỚC 1 — Bind CacheOptions + tạo logger startup ngắn hạn + đăng ký singleton trạng thái backend.
    // BƯỚC 2 — Nếu PreferInProcessCache: chỉ AddDistributedMemoryCache và return.
    // BƯỚC 3 — Nếu thiếu ConnectionStrings:Redis: memory và return.
    // BƯỚC 4 — Đăng ký RedisCache + MemoryDistributedCache + PrefixedMemory + composite IDistributedCache.
    public static void AddDistributedCaching(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<CacheOptions>(builder.Configuration.GetSection(CacheOptions.SectionName));
        var cacheOptions = builder.Configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>() ?? new();

        using var loggerFactory = CreateProbeLoggerFactory(builder);
        var log = loggerFactory.CreateLogger("CommentAPI.Cache");
        builder.Services.AddSingleton<CacheBackendState>();
        builder.Services.AddSingleton<CacheBackendDescriptor>(sp =>
            new CacheBackendDescriptor(sp.GetRequiredService<CacheBackendState>()));

        if (cacheOptions.PreferInProcessCache)
        {
            log.LogInformation(
                "Cache: {Name}=true — chỉ dùng memory trong process (DistributedMemory); bỏ qua Redis.",
                nameof(CacheOptions.PreferInProcessCache));
            builder.Services.AddDistributedMemoryCache();
            return;
        }

        var rawCs = builder.Configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(rawCs))
        {
            log.LogWarning("Cache: ConnectionStrings:Redis chưa cấu hình — dùng memory trong process.");
            builder.Services.AddDistributedMemoryCache();
            return;
        }

        var connectionString = EnsureAbortConnectNotBlocking(rawCs);
        const string instanceName = "CommentAPI:";

        builder.Services.Configure<RedisCacheOptions>(o =>
        {
            o.Configuration = connectionString;
            o.InstanceName = instanceName;
        });

        builder.Services.AddSingleton<RedisCache>();
        builder.Services.AddSingleton<MemoryDistributedCache>(sp =>
            new MemoryDistributedCache(
                Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions())));

        builder.Services.AddSingleton<PrefixedMemoryDistributedCache>(sp =>
            new PrefixedMemoryDistributedCache(
                sp.GetRequiredService<MemoryDistributedCache>(),
                instanceName));

        builder.Services.AddSingleton<IDistributedCache, RedisFirstFailoverMemoryDistributedCache>();

        log.LogInformation(
            "Cache: ưu tiên Redis (InstanceName {Instance}); fallback memory khi Redis lỗi; không cần restart để quay lại Redis.",
            instanceName);
    } // Kết thúc AddDistributedCaching.

    private static string EnsureAbortConnectNotBlocking(string connectionString)
    {
        if (connectionString.Contains("abortConnect", StringComparison.OrdinalIgnoreCase))
            return connectionString;
        return connectionString.TrimEnd(' ', ';') + ",abortConnect=false";
    }

    private static ILoggerFactory CreateProbeLoggerFactory(WebApplicationBuilder builder) =>
        LoggerFactory.Create(logging =>
        {
            logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
            logging.AddConsole();
        });
} // Kết thúc DistributedCaching.

public sealed class CacheBackendDescriptor
{
    private readonly CacheBackendState _state;

    public CacheBackendDescriptor(CacheBackendState state) => _state = state;
    public string Kind => _state.Kind;
} // Kết thúc CacheBackendDescriptor.
