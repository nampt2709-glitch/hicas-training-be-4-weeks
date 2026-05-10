using Asp.Versioning.ApiExplorer; // IApiVersionDescriptionProvider — mỗi version một group.
using Microsoft.Extensions.Options; // IConfigureOptions<SwaggerGenOptions>.
using Microsoft.OpenApi.Models; // OpenApiInfo Title/Version.
using Swashbuckle.AspNetCore.SwaggerGen; // SwaggerGenOptions SwaggerDoc.

namespace ApartmentAPI.Configuration;

// Đăng ký một Swagger document cho mỗi API version (ApiExplorer sinh group từ Asp.Versioning).
public sealed class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{ // Mở khối ConfigureSwaggerOptions.
    private readonly IApiVersionDescriptionProvider _provider; // Mô tả v1/v2 + deprecated flags.

    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
    { // Mở khối constructor.
        _provider = provider; // Inject provider từ AddApiVersioning+ApiExplorer.
    } // Kết thúc constructor.

    // Mỗi ApiVersionDescription → SwaggerDoc(groupName, OpenApiInfo).
    public void Configure(SwaggerGenOptions options)
    { // Mở khối Configure.
        foreach (var description in _provider.ApiVersionDescriptions)
        { // BƯỚC 1 — Lặp mọi version Asp.Versioning biết được.
            options.SwaggerDoc(description.GroupName, new OpenApiInfo
            {
                Title = "ApartmentAPI", // Tên hiển thị UI Swagger.
                Version = description.ApiVersion.ToString(), // Chuỗi version (1.0, 2.0, ...).
                Description = description.IsDeprecated
                    ? "ApartmentAPI — this API version is deprecated." // Cờ deprecated trên doc.
                    : "ApartmentAPI REST (versioned).", // Mô tả chuẩn.
            });
        }
    } // Kết thúc Configure.
} // Kết thúc ConfigureSwaggerOptions.
