using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ApartmentAPI.Configuration;

// Bổ sung mô tả tham số / mặc định cho Swagger khi dùng chung với API Versioning.
public sealed class SwaggerDefaultValues : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var apiDescription = context.ApiDescription;

        operation.Deprecated |= apiDescription.IsDeprecated();

        if (operation.Parameters is null)
        {
            return;
        }

        foreach (var parameter in operation.Parameters)
        {
            var description = apiDescription.ParameterDescriptions
                .FirstOrDefault(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
            if (description is null)
            {
                continue;
            }

            parameter.Description ??= description.ModelMetadata?.Description;

            if (parameter.Schema.Default is not null && parameter.Description is not null)
            {
                parameter.Description += $" (mặc định: {parameter.Schema.Default})";
            }

            parameter.Required |= description.IsRequired;
        }
    }
}
