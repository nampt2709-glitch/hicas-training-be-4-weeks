using Microsoft.AspNetCore.Mvc.ApiExplorer; // ApiDescription: IsDeprecated + ParameterDescriptions.
using Microsoft.OpenApi.Models; // OpenApiOperation.Parameters, Schema.Default.
using Swashbuckle.AspNetCore.SwaggerGen; // IOperationFilter, OperationFilterContext.

namespace CommentAPI.Configuration;

// Bộ lọc Swagger: đồng bộ Deprecated + mô tả tham số + Required từ model metadata (API Versioning + binding).
public sealed class SwaggerDefaultValues : IOperationFilter
{
    // BƯỚC 1 — Lấy ApiDescription từ context; gộp cờ deprecated vào operation nếu version đánh dấu deprecated.
    // BƯỚC 2 — Duyệt từng OpenApi parameter: ghép Description từ ModelMetadata (và ghép default nếu có).
    // BƯỚC 3 — OR với Required từ binding metadata (để Swagger UI không bỏ sót tham số bắt buộc).
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var apiDescription = context.ApiDescription;
        operation.Deprecated |= apiDescription.IsDeprecated();

        if (operation.Parameters is null)
            return;

        foreach (var parameter in operation.Parameters)
        {
            var description = apiDescription.ParameterDescriptions
                .FirstOrDefault(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
            if (description is null)
                continue;

            parameter.Description ??= description.ModelMetadata?.Description;

            if (parameter.Schema.Default is not null && parameter.Description is not null)
                parameter.Description += $" (default: {parameter.Schema.Default})";

            parameter.Required |= description.IsRequired;
        }
    } // Kết thúc Apply.
} // Kết thúc SwaggerDefaultValues.
