using Microsoft.AspNetCore.Mvc.ApiExplorer; // ApiDescription ParameterDescriptions.
using Microsoft.OpenApi.Models; // OpenApiOperation Parameters.
using Swashbuckle.AspNetCore.SwaggerGen; // IOperationFilter.

namespace ApartmentAPI.Configuration;

// Bổ sung mô tả tham số / mặc định cho Swagger khi dùng chung với API Versioning.
public sealed class SwaggerDefaultValues : IOperationFilter
{ // Mở khối SwaggerDefaultValues.
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    { // Mở khối Apply.
        var apiDescription = context.ApiDescription;

        // BƯỚC 1 — Cờ deprecated từ ApiDescription OR vào operation.
        operation.Deprecated |= apiDescription.IsDeprecated();

        if (operation.Parameters is null)
        { // TRƯỜNG HỢP A — Không có parameter OpenAPI.
            return;
        }

        // BƯỚC 2 — Với mỗi parameter: map description, gắn default text, Required từ model binding.
        foreach (var parameter in operation.Parameters)
        {
            var description = apiDescription.ParameterDescriptions
                .FirstOrDefault(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
            if (description is null)
            { // Không khớp ApiExplorer — bỏ qua parameter này.
                continue;
            }

            parameter.Description ??= description.ModelMetadata?.Description; // XML doc / metadata.

            if (parameter.Schema.Default is not null && parameter.Description is not null)
            { // Nếu có default trong schema — chú thích thêm cho UI.
                parameter.Description += $" (default: {parameter.Schema.Default})";
            }

            parameter.Required |= description.IsRequired; // Bắt buộc theo binding.
        }
    } // Kết thúc Apply.
} // Kết thúc SwaggerDefaultValues.
