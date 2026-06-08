using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Runnatics.Api.Filters
{
    /// <summary>
    /// Adds X-Device-Key as a required security scheme on all /api/pidevice/* and live-readings endpoints.
    /// </summary>
    public class DeviceKeyOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var path = context.ApiDescription.RelativePath ?? string.Empty;

            var isPiEndpoint =
                path.StartsWith("api/pidevice", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("live-readings", StringComparison.OrdinalIgnoreCase);

            if (!isPiEndpoint)
                return;

            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "X-Device-Key"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        }
    }
}
