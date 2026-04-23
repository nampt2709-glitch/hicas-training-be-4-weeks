using System.Collections.Frozen;

namespace CommentAPI.Configuration;

/// <summary>
/// Cấu hình rate limit tập trung cho toàn bộ route trong CommentAPI.
/// Mỗi route được định danh bởi cặp HTTP method + route pattern (template endpoint).
/// </summary>
public static class RouteRateLimitConfiguration
{
    /// <summary>
    /// Rule mặc định áp dụng khi route không có cấu hình riêng.
    /// </summary>
    public static readonly RouteRateLimitRule DefaultRule = new(
        Key: "default",
        PermitLimit: 120,
        WindowSeconds: 60,
        QueueLimit: 0);

    /// <summary>
    /// Danh sách rule per-route. Đây là file duy nhất cần chỉnh khi thay đổi giới hạn.
    /// </summary>
    private static readonly FrozenDictionary<string, RouteRateLimitRule> Rules = new Dictionary<string, RouteRateLimitRule>(StringComparer.OrdinalIgnoreCase)
    {
        // AuthController
        [BuildRouteKey("POST", "api/auth/signup")] = new("POST api/auth/signup", 5, 60, 0),
        [BuildRouteKey("POST", "api/auth/login")] = new("POST api/auth/login", 10, 60, 0),
        [BuildRouteKey("POST", "api/auth/refresh")] = new("POST api/auth/refresh", 20, 60, 0),
        [BuildRouteKey("POST", "api/auth/logout")] = new("POST api/auth/logout", 60, 60, 0),

        // UsersController
        [BuildRouteKey("GET", "api/users")] = new("GET api/users", 60, 60, 0),
        [BuildRouteKey("GET", "api/users/{id:guid}")] = new("GET api/users/{id:guid}", 90, 60, 0),
        [BuildRouteKey("POST", "api/users")] = new("POST api/users", 20, 60, 0),
        [BuildRouteKey("PUT", "api/users/{id:guid}")] = new("PUT api/users/{id:guid}", 30, 60, 0),
        [BuildRouteKey("PUT", "api/admin/users/{id:guid}")] = new("PUT api/admin/users/{id:guid}", 30, 60, 0),
        [BuildRouteKey("DELETE", "api/users/{id:guid}")] = new("DELETE api/users/{id:guid}", 10, 60, 0),

        // PostsController
        [BuildRouteKey("GET", "api/posts")] = new("GET api/posts", 90, 60, 0),
        [BuildRouteKey("GET", "api/posts/{id:guid}")] = new("GET api/posts/{id:guid}", 120, 60, 0),
        [BuildRouteKey("POST", "api/posts")] = new("POST api/posts", 20, 60, 0),
        [BuildRouteKey("PUT", "api/posts/{id:guid}")] = new("PUT api/posts/{id:guid}", 30, 60, 0),
        [BuildRouteKey("PUT", "api/admin/posts/{id:guid}")] = new("PUT api/admin/posts/{id:guid}", 30, 60, 0),
        [BuildRouteKey("DELETE", "api/posts/{id:guid}")] = new("DELETE api/posts/{id:guid}", 10, 60, 0),

        // CommentsController
        [BuildRouteKey("GET", "api/comments")] = new("GET api/comments", 120, 60, 0),
        [BuildRouteKey("GET", "api/comments/{id:guid}")] = new("GET api/comments/{id:guid}", 150, 60, 0),
        [BuildRouteKey("GET", "api/comments/user/{userId:guid}")] = new("GET api/comments/user/{userId:guid}", 120, 60, 0),
        [BuildRouteKey("POST", "api/comments")] = new("POST api/comments", 40, 60, 0),
        [BuildRouteKey("PUT", "api/comments/{id:guid}")] = new("PUT api/comments/{id:guid}", 40, 60, 0),
        [BuildRouteKey("PUT", "api/admin/comments/{id:guid}")] = new("PUT api/admin/comments/{id:guid}", 40, 60, 0),
        [BuildRouteKey("DELETE", "api/comments/{id:guid}")] = new("DELETE api/comments/{id:guid}", 20, 60, 0),
        [BuildRouteKey("GET", "api/comments/flat")] = new("GET api/comments/flat", 90, 60, 0),
        [BuildRouteKey("GET", "api/comments/cte")] = new("GET api/comments/cte", 60, 60, 0),
        [BuildRouteKey("GET", "api/comments/tree/flat")] = new("GET api/comments/tree/flat", 50, 60, 0),
        [BuildRouteKey("GET", "api/comments/tree/cte")] = new("GET api/comments/tree/cte", 40, 60, 0),
        [BuildRouteKey("GET", "api/comments/tree/flat/flatten")] = new("GET api/comments/tree/flat/flatten", 60, 60, 0),
        [BuildRouteKey("GET", "api/comments/tree/cte/flatten")] = new("GET api/comments/tree/cte/flatten", 50, 60, 0),
        [BuildRouteKey("GET", "api/comments/demo/lazy-loading")] = new("GET api/comments/demo/lazy-loading", 25, 60, 0),
        [BuildRouteKey("GET", "api/comments/demo/eager-loading")] = new("GET api/comments/demo/eager-loading", 25, 60, 0),
        [BuildRouteKey("GET", "api/comments/demo/explicit-loading")] = new("GET api/comments/demo/explicit-loading", 25, 60, 0),
        [BuildRouteKey("GET", "api/comments/demo/projection")] = new("GET api/comments/demo/projection", 30, 60, 0),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tìm rule theo method + route pattern. Nếu không có, trả về rule mặc định.
    /// </summary>
    public static RouteRateLimitRule Resolve(string? method, string? routePattern)
    {
        var key = BuildRouteKey(method, routePattern);
        return Rules.TryGetValue(key, out var rule) ? rule : DefaultRule;
    }

    /// <summary>
    /// Chuẩn hóa route key để map ổn định giữa cấu hình và endpoint metadata.
    /// </summary>
    private static string BuildRouteKey(string? method, string? routePattern)
    {
        var normalizedMethod = (method ?? "GET").Trim().ToUpperInvariant();
        var normalizedRoute = NormalizeRoutePattern(routePattern);
        return $"{normalizedMethod}:{normalizedRoute}";
    }

    /// <summary>
    /// Chuẩn hóa route template: bỏ dấu "/" đầu/cuối và đổi về lower.
    /// </summary>
    private static string NormalizeRoutePattern(string? routePattern)
    {
        if (string.IsNullOrWhiteSpace(routePattern))
        {
            return string.Empty;
        }

        return routePattern.Trim().Trim('/').ToLowerInvariant();
    }
}

/// <summary>
/// Mô tả cấu hình giới hạn cho một route.
/// </summary>
public sealed record RouteRateLimitRule(string Key, int PermitLimit, int WindowSeconds, int QueueLimit);
