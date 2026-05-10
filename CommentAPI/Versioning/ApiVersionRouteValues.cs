using Asp.Versioning; // IApiVersioningFeature — đọc version request cho CreatedAtAction.
using Microsoft.AspNetCore.Mvc; // ControllerBase, RouteValueDictionary.
using Microsoft.AspNetCore.Routing; // RouteValueDictionary merge route values.

namespace CommentAPI.Versioning;

// Gắn segment version vào RouteValues cho CreatedAtAction (giống ApartmentAPI).
public static class ApiVersionRouteValues
{
    // Ghép routeValues với khóa "version" từ RouteData hoặc feature API versioning.
    public static RouteValueDictionary WithVersion(ControllerBase controller, object routeValues)
    { // Mở khối WithVersion.
        // BƯỚC 1 — Sao chép routeValues vào dictionary có thể sửa.
        var dict = new RouteValueDictionary(routeValues); // Khởi tạo từ anonymous object hoặc dict.

        // TRƯỜNG HỢP A — Caller đã truyền sẵn version: không ghi đè.
        if (dict.ContainsKey("version")) // Đã có version.
            return dict; // Trả nguyên.

        // BƯỚC 2 — Thử lấy version từ RouteData hiện tại (URL segment api/v{version}/…).
        if (controller.RouteData.Values.TryGetValue("version", out var rv) && rv is not null) // Có giá trị.
        { // Mở nhánh RouteData.
            dict["version"] = rv; // Gán vào dict trả về CreatedAtAction.
            return dict; // Xong.
        } // Kết thúc RouteData.

        // BƯỚC 3 — Fallback: đọc IApiVersioningFeature hoặc mặc định "1.0".
        var requested = controller.HttpContext?.Features.Get<IApiVersioningFeature>()?.RequestedApiVersion; // Version đã parse middleware.
        dict["version"] = requested?.ToString() ?? "1.0"; // Chuỗi cho URL segment.
        return dict; // Dictionary đủ version.
    } // Kết thúc WithVersion.
} // Kết thúc ApiVersionRouteValues.
