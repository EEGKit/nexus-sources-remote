using Asp.Versioning;
using Nexus.Agent;
using Nexus.Core;
using Nexus.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder();

var configuration = NexusOptionsBase.BuildConfiguration();
builder.Configuration.AddConfiguration(configuration);

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

builder.Services.Configure<PathsOptions>(configuration.GetSection(PathsOptions.Section));

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapControllers();

var pathsOptions = configuration
    .GetRequiredSection(PathsOptions.Section)
    .Get<PathsOptions>() ?? throw new Exception("Unable to instantiate path options");

var agent = new Agent(pathsOptions);
var extensionHive = await agent.LoadPackagesAsync();
_ = agent.AcceptClientsAsync(extensionHive);

app.Run();
