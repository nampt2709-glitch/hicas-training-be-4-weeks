using System.Collections.Frozen; // FrozenDictionary lookup nhanh cho rule rate limit.

namespace ApartmentAPI.Configuration;

// Rate limit theo method + route template (giống CommentAPI; route có version segment api/v{version:apiVersion}/...).
public static class RouteRateLimitConfiguration
{ // Mở khối RouteRateLimitConfiguration.
    // Thang permit cố định: biến môi trường RATE_LIMIT_PERMIT_MULTIPLIER (vd k6 load test).
    private static int GetPermitMultiplier() =>
        int.TryParse(Environment.GetEnvironmentVariable("RATE_LIMIT_PERMIT_MULTIPLIER"), out var m) && m > 0
            ? m
            : 1;

    // Nhân permit với multiplier rồi Math.Max(1, ...) — tránh 0 permit.
    private static RouteRateLimitRule ScaleRule(string key, int permitLimit, int windowSeconds = 60, int queueLimit = 0) =>
        new(key, Math.Max(1, permitLimit * GetPermitMultiplier()), windowSeconds, queueLimit);

    public static readonly RouteRateLimitRule DefaultRule = ScaleRule("default", 120, 60, 0); // Mặc định toàn API.

    // Từ điển băng GET/POST + route đã chuẩn hóa → rule riêng (auth endpoints chặt hơn).
    private static readonly FrozenDictionary<string, RouteRateLimitRule> Rules = new Dictionary<string, RouteRateLimitRule>(StringComparer.OrdinalIgnoreCase)
    {
        [BuildRouteKey("POST", "api/auth/signup")] = ScaleRule("POST api/auth/signup", 5, 60, 0),
        [BuildRouteKey("POST", "api/auth/login")] = ScaleRule("POST api/auth/login", 10, 60, 0),
        [BuildRouteKey("POST", "api/auth/refresh")] = ScaleRule("POST api/auth/refresh", 20, 60, 0),
        [BuildRouteKey("POST", "api/auth/logout")] = ScaleRule("POST api/auth/logout", 60, 60, 0),
        // Cùng hạn mức cho bản có segment phiên bản (V1/V2).
        [BuildRouteKey("POST", "api/v{version:apiVersion}/auth/signup")] = ScaleRule("POST api/v{version:apiVersion}/auth/signup", 5, 60, 0),
        [BuildRouteKey("POST", "api/v{version:apiVersion}/auth/login")] = ScaleRule("POST api/v{version:apiVersion}/auth/login", 10, 60, 0),
        [BuildRouteKey("POST", "api/v{version:apiVersion}/auth/refresh")] = ScaleRule("POST api/v{version:apiVersion}/auth/refresh", 20, 60, 0),
        [BuildRouteKey("POST", "api/v{version:apiVersion}/auth/logout")] = ScaleRule("POST api/v{version:apiVersion}/auth/logout", 60, 60, 0),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // Tra rule theo method + route pattern endpoint hiện tại — fallback DefaultRule.
    public static RouteRateLimitRule Resolve(string? method, string? routePattern)
    { // Mở khối Resolve.
        var key = BuildRouteKey(method, routePattern);
        return Rules.TryGetValue(key, out var rule) ? rule : DefaultRule;
    } // Kết thúc Resolve.

    private static string BuildRouteKey(string? method, string? routePattern) =>
        $"{(method ?? "GET").Trim().ToUpperInvariant()}:{NormalizeRoutePattern(routePattern)}";

    private static string NormalizeRoutePattern(string? routePattern) =>
        string.IsNullOrWhiteSpace(routePattern) ? string.Empty : routePattern.Trim().Trim('/').ToLowerInvariant();
} // Kết thúc RouteRateLimitConfiguration.

// Bộ ba cố định cửa sổ rate limit — middleware đọc PermitLimit/WindowSeconds/QueueLimit.
public sealed record RouteRateLimitRule(string Key, int PermitLimit, int WindowSeconds, int QueueLimit);
