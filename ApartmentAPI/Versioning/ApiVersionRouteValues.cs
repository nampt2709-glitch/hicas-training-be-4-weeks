using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace ApartmentAPI.Versioning;

// Gắn segment version vào RouteValues cho CreatedAtAction; ưu tiên route, sau đó version Asp.Versioning đã resolve cho request.
public static class ApiVersionRouteValues
{
    public static RouteValueDictionary WithVersion(ControllerBase controller, object routeValues)
    {
        var dict = new RouteValueDictionary(routeValues);
        if (dict.ContainsKey("version"))
        {
            return dict;
        }

        if (controller.RouteData.Values.TryGetValue("version", out var rv) && rv is not null)
        {
            dict["version"] = rv;
            return dict;
        }

        var requested = controller.HttpContext?.Features.Get<IApiVersioningFeature>()?.RequestedApiVersion;
        dict["version"] = requested?.ToString() ?? "1.0";
        return dict;
    }
}
