using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace CommentAPI;

/// <summary>Cấu hình TTL cho cache entity (JSON trong distributed cache).</summary>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>Thời gian sống mặc định của mỗi key entity (giây).</summary>
    public int EntityTtlSeconds { get; set; } = 120;
}

/// <summary>Loại backend cache đang dùng toàn ứng dụng (redis hoặc memory) — để gắn header response.</summary>
public sealed class CacheBackendDescriptor
{
    public CacheBackendDescriptor(string kind) => Kind = kind;

    /// <summary>Giá trị header: <c>redis</c> hoặc <c>memory</c>.</summary>
    public string Kind { get; }
}

/// <summary>
/// Ghi nhận HIT/MISS theo từng HTTP request qua DI scoped — không ghi vào HttpContext.Items
/// (tránh mất tương quan request sau ConfigureAwait(false) trong pipeline).
/// </summary>
public sealed class CacheResponseTracker
{
    /// <summary>Đã có ít nhất một thao tác đọc cache trong request.</summary>
    public bool LookupPerformed { get; private set; }

    /// <summary><c>true</c> = HIT, <c>false</c> = MISS (chỉ meaningful khi <see cref="LookupPerformed"/>).</summary>
    public bool WasHit { get; private set; }

    /// <summary>Ghi nhận kết quả một lần đọc cache (lần gọi sau trong cùng request ghi đè).</summary>
    public void ReportLookup(bool cacheHit)
    {
        LookupPerformed = true;
        WasHit = cacheHit;
    }
}

/// <summary>Băm ngắn chuỗi tìm kiếm để khóa Redis gọn và an toàn độ dài.</summary>
public static class EntityCacheHash
{
    public static string SearchTerm(string normalizedTerm)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedTerm));
        return Convert.ToHexString(bytes.AsSpan(0, 8));
    }
}

/// <summary>Tiện ích tạo khóa cache theo entity và theo trang.</summary>
public static class EntityCacheKeys
{
    public static string User(Guid id) => $"u:{id:N}";
    public static string Post(Guid id) => $"p:{id:N}";
    public static string Comment(Guid id) => $"c:{id:N}";

    public static string UsersPaged(int page, int pageSize) => $"l:users:{page}:{pageSize}";
    public static string UsersSearchName(string termHash, int page, int pageSize) => $"l:users:sn:{termHash}:{page}:{pageSize}";
    public static string UsersSearchUserName(string termHash, int page, int pageSize) => $"l:users:su:{termHash}:{page}:{pageSize}";

    public static string PostsPaged(int page, int pageSize) => $"l:posts:{page}:{pageSize}";
    public static string PostsSearchTitle(string termHash, int page, int pageSize) => $"l:posts:st:{termHash}:{page}:{pageSize}";

    public static string CommentsAll(int page, int pageSize) => $"l:comments:all:{page}:{pageSize}";
    public static string CommentsSearchContent(string termHash, int page, int pageSize) => $"l:comments:sc:{termHash}:{page}:{pageSize}";

    public static string CommentsSearchContentInPost(Guid postId, string termHash, int page, int pageSize) =>
        $"l:comments:p:{postId:N}:sc:{termHash}:{page}:{pageSize}";
    public static string CommentsAllTreeFlat(int page, int pageSize) => $"l:comments:tree:flat:{page}:{pageSize}";
    public static string CommentsAllFlattenEfTree(int page, int pageSize) => $"l:comments:flat:eftree:{page}:{pageSize}";
    public static string CommentsAllFlattenCteTree(int page, int pageSize) => $"l:comments:flat:ctetree:{page}:{pageSize}";
    public static string CommentsAllCteFlat(int page, int pageSize) => $"l:comments:cteflat:{page}:{pageSize}";

    public static string CommentsFlatByPost(Guid postId, int page, int pageSize) =>
        $"l:comments:p:{postId:N}:flat:{page}:{pageSize}";

    public static string CommentsCteFlatByPost(Guid postId, int page, int pageSize) =>
        $"l:comments:p:{postId:N}:cteflat:{page}:{pageSize}";

    public static string CommentsTreeByPost(Guid postId, int page, int pageSize) =>
        $"l:comments:p:{postId:N}:tree:{page}:{pageSize}";

    public static string CommentsFlattenedCteTree(Guid postId, int page, int pageSize) =>
        $"l:comments:p:{postId:N}:ctetree:{page}:{pageSize}";

    public static string CommentsFlattenedEfTreeByPost(Guid postId, int page, int pageSize) =>
        $"l:comments:p:{postId:N}:eftreeflat:{page}:{pageSize}";
}

/// <summary>Đọc/ghi DTO dạng JSON trong <see cref="IDistributedCache"/> và báo HIT/MISS qua <see cref="CacheResponseTracker"/>.</summary>
public interface IEntityResponseCache
{
    Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    Task SetJsonAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class;

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
}

/// <summary>Triển khai cache-aside cho DTO / trang phân trang.</summary>
public sealed class EntityResponseCache : IEntityResponseCache
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IDistributedCache _cache;
    private readonly CacheResponseTracker _tracker;
    private readonly TimeSpan _ttl;

    public EntityResponseCache(
        IDistributedCache cache,
        CacheResponseTracker tracker,
        IOptions<CacheOptions> options)
    {
        _cache = cache;
        _tracker = tracker;
        var sec = Math.Clamp(options.Value.EntityTtlSeconds, 30, 86_400);
        _ttl = TimeSpan.FromSeconds(sec);
    }

    /// <inheritdoc />
    public async Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        // Đọc chuỗi JSON từ Redis hoặc memory; báo HIT/MISS qua tracker scoped (header X-Cache-Status).
        var raw = await _cache.GetStringAsync(key, cancellationToken);
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
    }

    /// <inheritdoc />
    public Task SetJsonAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class
    {
        var raw = JsonSerializer.Serialize(value, Json);
        return _cache.SetStringAsync(
            key,
            raw,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        _cache.RemoveAsync(key, cancellationToken);

    /// <inheritdoc />
    public async Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        foreach (var key in keys)
        {
            await _cache.RemoveAsync(key, cancellationToken);
        }
    }
}
