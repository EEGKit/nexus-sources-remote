using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Nexus.Agent.Core;
using NJsonSchema.Generation;
using NSwag.AspNetCore;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.DependencyInjection;

internal static class AgentOpenApiExtensions
{
    public static IServiceCollection AddAgentOpenApi(
        this IServiceCollection services
    )
    {
        // https://github.com/dotnet/aspnet-api-versioning/tree/master/samples/aspnetcore/SwaggerSample
        services
            .AddControllers()
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .ConfigureApplicationPartManager(
                manager => manager.FeatureProviders.Add(new InternalControllerFeatureProvider())
            );

        services.AddApiVersioning(
            options =>
            {
                options.ReportApiVersions = true;
            });

        services.AddVersionedApiExplorer(
            options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        /* not optimal */
        var provider = services.BuildServiceProvider().GetRequiredService<IApiVersionDescriptionProvider>();

        foreach (var description in provider.ApiVersionDescriptions)
        {
            services.AddOpenApiDocument(config =>
            {
                config.SchemaSettings.DefaultReferenceTypeNullHandling = ReferenceTypeNullHandling.NotNull;

                config.Title = "Nexus.Agent REST API";
                config.Version = description.GroupName;
                config.Description = "Manage package references."
                    + (description.IsDeprecated ? " This API version is deprecated." : "");

                config.ApiGroupNames = [description.GroupName];
                config.DocumentName = description.GroupName;
            });
        }

        return services;
    }

    public static WebApplication UseAgentOpenApi(
        this WebApplication app,
        IApiVersionDescriptionProvider provider
    )
    {
        app.UseOpenApi(settings => settings.Path = "/openapi/{documentName}.json");

        app.UseSwaggerUi(settings =>
        {
            settings.Path = "/api";

            foreach (var description in provider.ApiVersionDescriptions)
            {
                settings.SwaggerRoutes.Add(
                    new SwaggerUiRoute(
                        description.GroupName.ToUpperInvariant(),
                        $"/openapi/{description.GroupName}.json"));
            }
        });

        return app;
    }
}
