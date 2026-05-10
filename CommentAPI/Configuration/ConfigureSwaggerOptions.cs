using Asp.Versioning.ApiExplorer; // IApiVersionDescriptionProvider — mô tả từng API version.
using Microsoft.Extensions.Options; // IConfigureOptions pattern.
using Microsoft.OpenApi.Models; // OpenApiInfo cho mỗi SwaggerDoc.
using Swashbuckle.AspNetCore.SwaggerGen; // SwaggerGenOptions.

namespace CommentAPI.Configuration;

// Đăng ký một OpenAPI document (swagger.json) cho mỗi nhóm version từ ApiExplorer.
public sealed class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{ // Mở lớp ConfigureSwaggerOptions.
    private readonly IApiVersionDescriptionProvider _provider; // Nguồn danh sách version + deprecated flag.

    // BƯỚC 1 — Tiêm provider từ hosting (AddApiVersioning + AddApiExplorer).
    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider) =>
        _provider = provider; // Lưu để Configure lặp qua ApiVersionDescriptions.

    // BƯỚC 1 — Duyệt mọi description (v1, v2, …) lấy từ ApiExplorer.
    // BƯỚC 2 — Với mỗi nhóm version: SwaggerDoc(GroupName, OpenApiInfo Title/Version/Description; gắn cờ deprecated nếu có).
    public void Configure(SwaggerGenOptions options)
    { // Mở Configure.
        foreach (var description in _provider.ApiVersionDescriptions) // Mỗi version một document.
        { // Mở vòng foreach.
            options.SwaggerDoc(description.GroupName, new OpenApiInfo // Tên nhóm = v1, v2, …
            {
                Title = "CommentAPI",
                Version = description.ApiVersion.ToString(), // Chuỗi version hiển thị.
                Description = description.IsDeprecated
                    ? "CommentAPI — this API version is deprecated."
                    : "CommentAPI REST (versioned).",
            });
        } // Kết foreach.
    } // Kết thúc Configure.
} // Kết thúc lớp ConfigureSwaggerOptions.
