using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ameen.Erp.Api;

public static class OpenApiConfiguration
{
    public static void ConfigureErp(this OpenApiOptions options)
    {
        options.AddSchemaTransformer((schema, context, ct) =>
        {
            if (context.JsonTypeInfo.Type == typeof(decimal))
            {
                schema.Type = JsonSchemaType.String; schema.Format = "decimal";
                schema.Pattern = @"^-?[0-9]+(\.[0-9]+)?$";
                schema.Description = "Exact base-10 decimal string; preserve precision in the client.";
            }
            if (context.JsonTypeInfo.Type == typeof(DateOnly))
            {
                schema.Type = JsonSchemaType.String; schema.Format = "date";
            }
            return Task.CompletedTask;
        });
        options.AddOperationTransformer((operation, context, ct) =>
        {
            if (context.Description.HttpMethod is "POST" or "PUT" &&
                context.Description.RelativePath?.StartsWith("api/v1/", StringComparison.Ordinal) == true &&
                context.Description.RelativePath?.EndsWith("/journals/process-batch", StringComparison.Ordinal) != true &&
                context.Description.RelativePath?.EndsWith("/journals/reversals/run-due", StringComparison.Ordinal) != true &&
                context.Description.RelativePath?.EndsWith("/recurring-journals/run-due", StringComparison.Ordinal) != true)
            {
                operation.Parameters ??= new List<IOpenApiParameter>();
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "Idempotency-Key", In = ParameterLocation.Header, Required = true,
                    Description = "Unique logical command key. Replay with the same actor, operation and body.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = 100 }
                });
            }
            return Task.CompletedTask;
        });
        options.AddDocumentTransformer((document, context, ct) =>
        {
            document.Info.Title = "Ameen ERP API"; document.Info.Version = "v1";
            document.Servers = new List<OpenApiServer> { new() { Url = "/" } };
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT"
            };
            foreach (var path in document.Paths.Where(p => p.Key != "/health/live"))
            foreach (var operation in path.Value.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
            {
                operation.Security ??= new List<OpenApiSecurityRequirement>();
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
                });
            }
            return Task.CompletedTask;
        });
    }
}
