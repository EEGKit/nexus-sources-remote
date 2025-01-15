using Asp.Versioning;
using Microsoft.Extensions.Options;
using Nexus.Agent;
using Nexus.Core;
using Nexus.PackageManagement.Core;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder();

var configuration = NexusAgentOptions.BuildConfiguration();
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
    .AddSingleton<AgentService>();

builder.Services.Configure<SystemOptions>(configuration.GetSection(SystemOptions.Section));
builder.Services.Configure<PathsOptions>(configuration.GetSection(PathsOptions.Section));

// Package management
builder.Services.AddPackageManagement();

builder.Services.AddSingleton<IPackageManagementPathsOptions>(
    serviceProvider => serviceProvider.GetRequiredService<IOptions<PathsOptions>>().Value);

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapControllers();

var pathsOptions = app.Services.GetRequiredService<IOptions<PathsOptions>>();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Current directory: {CurrentDirectory}", Environment.CurrentDirectory);
logger.LogInformation("Loading configuration from path: {ConfigFolderPath}", pathsOptions.Value.Config);

var agent = app.Services.GetRequiredService<AgentService>();
await agent.LoadPackagesAsync(CancellationToken.None);
_ = agent.AcceptClientsAsync(CancellationToken.None);

app.Run();
