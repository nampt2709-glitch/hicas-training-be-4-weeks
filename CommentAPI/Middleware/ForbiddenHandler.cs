using System.Security.Claims; // ClaimTypes — đọc roles/sub khi log 403.
using CommentAPI; // ApiMessages, ApiErrorCodes.
using CommentAPI.Logging; // StructuredFileLogger.Security.
using Microsoft.AspNetCore.Authorization; // AuthorizationPolicy, PolicyAuthorizationResult.
using Microsoft.AspNetCore.Authorization.Policy; // IAuthorizationMiddlewareResultHandler.

namespace CommentAPI.Middleware;

// Đã xác thực nhưng thiếu quyền: trả 403 JSON thay vì body trống.
public sealed class ForbiddenHandler : IAuthorizationMiddlewareResultHandler // Thay thế xử lý mặc định khi Forbidden.
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new(); // Fallback handler chuẩn ASP.NET.

    public async Task HandleAsync( // Được framework gọi sau khi đánh giá policy.
        RequestDelegate next, // Delegate bước tiếp (không dùng khi trả 403 sớm).
        HttpContext context, // HTTP hiện tại.
        AuthorizationPolicy policy, // Policy đã áp.
        PolicyAuthorizationResult authorizeResult) // Kết quả authorize.
    {
        if (authorizeResult.Forbidden && !context.Response.HasStarted) // Thiếu quyền và response chưa flush.
        {
            var correlationId = RequestPerformanceMiddleware.GetCorrelationId(context); // Lấy hoặc tạo correlation id.
            context.Response.Headers.Append(RequestPerformanceMiddleware.HeaderName, correlationId); // Trả header cho client.
            RequestPerformanceMiddleware.AppendErrorSourceHeader(context, // Ghi nguồn lỗi vận hành.
                $"{nameof(ForbiddenHandler)} (authorization policy forbids this request)"); // Mô tả ngắn.
            RequestPerformanceMiddleware.TryAppendSqlQueryCountHeader(context); // Bổ sung số truy vấn SQL nếu có.
            context.Response.StatusCode = StatusCodes.Status403Forbidden; // HTTP 403.
            context.Response.ContentType = "application/json"; // JSON body.
            await context.Response // Ghi payload lỗi thống nhất.
                .WriteAsJsonAsync(new // Anonymous DTO.
                {
                    code = ApiErrorCodes.Forbidden, // Mã ứng dụng.
                    type = "AuthorizationFailed", // Loại lỗi cố định cho client.
                    message = ApiMessages.InsufficientPermission // Thông điệp người dùng.
                })
                .ConfigureAwait(false); // Tránh capture sync context.
            var uid = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ""; // User id nếu đã xác thực.
            var uname = context.User.Identity?.Name
                        ?? context.User.FindFirstValue(ClaimTypes.Name)
                        ?? context.User.FindFirstValue("unique_name")
                        ?? ""; // Tên hiển thị tốt nhất có được.
            StructuredFileLogger.Security( // Nhóm SECURITY: đủ auth nhưng role/policy không cho phép.
                correlationId,
                "ForbiddenInsufficientRole",
                context.Request.Method,
                context.Request.Path.Value ?? "",
                StatusCodes.Status403Forbidden,
                ApiMessages.InsufficientPermission,
                string.IsNullOrEmpty(uname) ? null : uname,
                string.IsNullOrEmpty(uid) ? null : uid);
            return; // Kết thúc pipeline tại đây.
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false); // Hành vi mặc định (200/401/…).
    }
}
