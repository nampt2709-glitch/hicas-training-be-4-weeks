using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ApartmentAPI.Configuration;

// Đăng ký một Swagger document cho mỗi API version (ApiExplorer sinh group từ Asp.Versioning).
public sealed class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{
    private readonly IApiVersionDescriptionProvider _provider;

    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
    {
        _provider = provider;
    }

    public void Configure(SwaggerGenOptions options)
    {
        foreach (var description in _provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc(description.GroupName, new OpenApiInfo
            {
                Title = "ApartmentAPI",
                Version = description.ApiVersion.ToString(),
                Description = description.IsDeprecated
                    ? "ApartmentAPI — phiên bản này đã deprecated."
                    : "ApartmentAPI REST (versioned).",
            });
        }
    }
}
