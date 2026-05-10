// File: helper RouteValues — gắn segment version cho CreatedAtAction (ưu tiên route đã resolve).
using Asp.Versioning; // IApiVersioningFeature: version request hiện tại.
using Microsoft.AspNetCore.Mvc; // ControllerBase, RouteData.
using Microsoft.AspNetCore.Routing; // RouteValueDictionary.

namespace ApartmentAPI.Versioning;

// Bổ sung key "version" vào dictionary route: từ routeValues đã có, RouteData, hoặc feature versioning.
public static class ApiVersionRouteValues
{ // Mở khối ApiVersionRouteValues.
    // Gộp routeValues với version (không ghi đè nếu caller đã truyền).
    public static RouteValueDictionary WithVersion(ControllerBase controller, object routeValues)
    { // Mở khối WithVersion.
        // BƯỚC 1 — Chuyển routeValues sang RouteValueDictionary để thêm/ghi key.
        var dict = new RouteValueDictionary(routeValues); // Dictionary có thể mutate.
        // TRƯỜNG HỢP 1 — Caller đã chỉ định "version": giữ nguyên, tránh đè.
        if (dict.ContainsKey("version")) // Đã có version trong object gốc.
        {
            return dict; // Trả về sớm — tôn trọng giá trị truyền vào.
        } // Kết thúc TRƯỜNG HỢP 1.

        // TRƯỜNG HỢP 2 — Route hiện tại đã có segment version (ví dụ api/v{version:apiVersion}/...).
        if (controller.RouteData.Values.TryGetValue("version", out var rv) && rv is not null) // version từ matched route.
        {
            dict["version"] = rv; // Gán object từ route (string hoặc kiểu constraint).
            return dict; // Ưu tiên route đã match trước feature.
        } // Kết thúc TRƯỜNG HỢP 2.

        // BƯỚC 2 — Fallback: đọc ApiVersion đã resolve từ pipeline; mặc định "1.0" nếu null.
        var requested = controller.HttpContext?.Features.Get<IApiVersioningFeature>()?.RequestedApiVersion; // Version object hoặc null.
        dict["version"] = requested?.ToString() ?? "1.0"; // Chuỗi hiển thị cho URL/helper.
        return dict; // Dictionary đã có version cho redirect/ CreatedAtAction.
    } // Kết thúc WithVersion.
} // Kết thúc ApiVersionRouteValues.
