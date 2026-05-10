using System.Collections.Frozen; // FrozenDictionary — bảng rule rate limit tra O(1) ổn định.

namespace CommentAPI.Configuration;

// =============================================================================
// File RouteRateLimitConfiguration.cs: cố định giới hạn FixedWindowRateLimiter theo từng cặp
// HTTP method + route template (sau versioning). GlobalLimiter trong Program gọi Resolve.
// =============================================================================
public static class RouteRateLimitConfiguration
{
    // Hệ số nhân số permit từ biến môi trường — dùng k6 / tải lớn lớp; production để 1.
    private static int GetPermitMultiplier() =>
        int.TryParse(Environment.GetEnvironmentVariable("RATE_LIMIT_PERMIT_MULTIPLIER"), out var m) && m > 0
            ? m
            : 1;

    private static RouteRateLimitRule ScaleRule(string key, int permitLimit, int windowSeconds = 60, int queueLimit = 0) =>
        new(key, Math.Max(1, permitLimit * GetPermitMultiplier()), windowSeconds, queueLimit);

    // Rule fallback khi route không có trong bảng — tránh PartitionedRateLimiter thiếu cấu hình.
    public static readonly RouteRateLimitRule DefaultRule = ScaleRule("default", 120, 60, 0);

    // Bảng FrozenDictionary tra cứu O(1) — key chuẩn hóa qua BuildRouteKey(METHOD, pattern).
    private static readonly FrozenDictionary<string, RouteRateLimitRule> Rules = new Dictionary<string, RouteRateLimitRule>(StringComparer.OrdinalIgnoreCase)
    {
        // AuthController — mỗi version v{version}
        [BuildRouteKey("POST", "api/v{version:apiVersion}/auth/signup")] = ScaleRule("POST api/v{version}/auth/signup", 5, 60, 0),
        [BuildRouteKey("POST", "api/v{version:apiVersion}/auth/login")] = ScaleRule("POST api/v{version}/auth/login", 10, 60, 0),
        [BuildRouteKey("POST", "api/v{version:apiVersion}/auth/refresh")] = ScaleRule("POST api/v{version}/auth/refresh", 20, 60, 0),
        [BuildRouteKey("POST", "api/v{version:apiVersion}/auth/logout")] = ScaleRule("POST api/v{version}/auth/logout", 60, 60, 0),

        [BuildRouteKey("GET", "api/v{version:apiVersion}/users")] = ScaleRule("GET api/v{version}/users", 60, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/users/{id:guid}")] = ScaleRule("GET api/v{version}/users/{id}", 90, 60, 0),
        [BuildRouteKey("POST", "api/v{version:apiVersion}/users")] = ScaleRule("POST api/v{version}/users", 20, 60, 0),
        [BuildRouteKey("PUT", "api/v{version:apiVersion}/users/{id:guid}")] = ScaleRule("PUT api/v{version}/users/{id}", 30, 60, 0),
        [BuildRouteKey("PUT", "api/v{version:apiVersion}/admin/users/{id:guid}")] = ScaleRule("PUT api/v{version}/admin/users/{id}", 30, 60, 0),
        [BuildRouteKey("DELETE", "api/v{version:apiVersion}/users/{id:guid}")] = ScaleRule("DELETE api/v{version}/users/{id}", 10, 60, 0),

        [BuildRouteKey("GET", "api/v{version:apiVersion}/posts")] = ScaleRule("GET api/v{version}/posts", 90, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/posts/{id:guid}")] = ScaleRule("GET api/v{version}/posts/{id}", 120, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/posts/{id:guid}/comments/tree")] = ScaleRule("GET api/v{version}/posts/{id}/comments/tree", 90, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/posts/{id:guid}/comments/flat")] = ScaleRule("GET api/v{version}/posts/{id}/comments/flat", 90, 60, 0),
        [BuildRouteKey("POST", "api/v{version:apiVersion}/posts")] = ScaleRule("POST api/v{version}/posts", 20, 60, 0),
        [BuildRouteKey("PUT", "api/v{version:apiVersion}/posts/{id:guid}")] = ScaleRule("PUT api/v{version}/posts/{id}", 30, 60, 0),
        [BuildRouteKey("PUT", "api/v{version:apiVersion}/admin/posts/{id:guid}")] = ScaleRule("PUT api/v{version}/admin/posts/{id}", 30, 60, 0),
        [BuildRouteKey("DELETE", "api/v{version:apiVersion}/posts/{id:guid}")] = ScaleRule("DELETE api/v{version}/posts/{id}", 10, 60, 0),

        [BuildRouteKey("GET", "api/v{version:apiVersion}/comments")] = ScaleRule("GET api/v{version}/comments", 120, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/comments/{id:guid}")] = ScaleRule("GET api/v{version}/comments/{id}", 150, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/comments/user/{userId:guid}")] = ScaleRule("GET api/v{version}/comments/user/{userId}", 120, 60, 0),
        [BuildRouteKey("POST", "api/v{version:apiVersion}/comments")] = ScaleRule("POST api/v{version}/comments", 40, 60, 0),
        [BuildRouteKey("PUT", "api/v{version:apiVersion}/comments/{id:guid}")] = ScaleRule("PUT api/v{version}/comments/{id}", 40, 60, 0),
        [BuildRouteKey("PUT", "api/v{version:apiVersion}/admin/comments/{id:guid}")] = ScaleRule("PUT api/v{version}/admin/comments/{id}", 40, 60, 0),
        [BuildRouteKey("DELETE", "api/v{version:apiVersion}/comments/{id:guid}")] = ScaleRule("DELETE api/v{version}/comments/{id}", 20, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/comments/flat")] = ScaleRule("GET api/v{version}/comments/flat", 90, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/comments/cte")] = ScaleRule("GET api/v{version}/comments/cte", 60, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/comments/tree/flat")] = ScaleRule("GET api/v{version}/comments/tree/flat", 50, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/comments/tree/cte")] = ScaleRule("GET api/v{version}/comments/tree/cte", 40, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/comments/tree/flat/flatten")] = ScaleRule("GET api/v{version}/comments/tree/flat/flatten", 60, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/comments/tree/cte/flatten")] = ScaleRule("GET api/v{version}/comments/tree/cte/flatten", 50, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/comments/flatten")] = ScaleRule("GET api/v{version}/comments/flatten", 50, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/comments/demo/lazy-loading")] = ScaleRule("GET api/v{version}/comments/demo/lazy-loading", 25, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/comments/demo/eager-loading")] = ScaleRule("GET api/v{version}/comments/demo/eager-loading", 25, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/comments/demo/explicit-loading")] = ScaleRule("GET api/v{version}/comments/demo/explicit-loading", 25, 60, 0),
        [BuildRouteKey("GET", "api/v{version:apiVersion}/comments/demo/projection")] = ScaleRule("GET api/v{version}/comments/demo/projection", 30, 60, 0),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // BƯỚC 1 — Chuẩn hóa method + routePattern thành key.
    // BƯỚC 2 — TryGetValue trong Rules; miss → DefaultRule (PartitionedRateLimiter nhận permit/window queue).
    public static RouteRateLimitRule Resolve(string? method, string? routePattern)
    {
        var key = BuildRouteKey(method, routePattern);
        return Rules.TryGetValue(key, out var rule) ? rule : DefaultRule;
    } // Kết thúc Resolve.

    private static string BuildRouteKey(string? method, string? routePattern)
    {
        var normalizedMethod = (method ?? "GET").Trim().ToUpperInvariant();
        var normalizedRoute = NormalizeRoutePattern(routePattern);
        return $"{normalizedMethod}:{normalizedRoute}";
    } // Kết thúc BuildRouteKey.

    private static string NormalizeRoutePattern(string? routePattern)
    {
        if (string.IsNullOrWhiteSpace(routePattern))
            return string.Empty;

        return routePattern.Trim().Trim('/').ToLowerInvariant();
    } // Kết thúc NormalizeRoutePattern.
} // Kết thúc RouteRateLimitConfiguration.

// Record: mô tả một rule — Key (nhãn log), PermitLimit, WindowSeconds, QueueLimit cho FixedWindowRateLimiterOptions.
public sealed record RouteRateLimitRule(string Key, int PermitLimit, int WindowSeconds, int QueueLimit);
