using Asp.Versioning;
using Nexus.Agent;
using Nexus.Core;
using Nexus.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder();

builder.Services

    .AddOpenApi()
    // .AddOpenApi("v2")

    .AddApiVersioning(config =>
    {
        config.ReportApiVersions = true;
        config.ApiVersionReader = new UrlSegmentApiVersionReader();
    })

    .AddApiExplorer(config =>
    {
        config.GroupNameFormat = "'v'VVV";
        config.SubstituteApiVersionInUrl = true;
    });

builder.Services
    .AddControllers()
    .ConfigureApplicationPartManager(
        manager => manager.FeatureProviders.Add(new InternalControllerFeatureProvider())
    );

builder.Services
    .AddSingleton<IDatabaseService, DatabaseService>()
    .AddSingleton<IPackageService, PackageService>()
    .AddSingleton<IExtensionHive, ExtensionHive>();

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapControllers();

var agent = new Agent();
_ = agent.RunAsync();

app.Run();
