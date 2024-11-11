// TODO
// - cancellation (RemoteCommunicator.RunAsync)
// - client logout / timeout
// - listen to localhost by default, make it configurable
// - rootless Podman example?
// - "src/Nexus.Agent" -> "src/agent/dotnet-agent/..."?
// - check all code ... especially correctness of namespaces of certain files

using Asp.Versioning;
using Nexus.Agent;
using Nexus.Core;
using Nexus.PackageManagement.Core;
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
    .AddSingleton<AgentService>();

builder.Services.Configure<PathsOptions>(configuration.GetSection(PathsOptions.Section));

// Package management
builder.Services.AddPackageManagement();
builder.Services.Configure<IPackageManagementPathsOptions>(x => configuration.GetSection(PathsOptions.Section).Bind(new PathsOptions()));

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapControllers();

var pathsOptions = configuration
    .GetRequiredSection(PathsOptions.Section)
    .Get<PathsOptions>() ?? throw new Exception("Unable to instantiate paths options");

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Current directory: {CurrentDirectory}", Environment.CurrentDirectory);
logger.LogInformation("Loading configuration from path: {ConfigFolderPath}", pathsOptions.Config);

var agent = app.Services.GetRequiredService<AgentService>();
await agent.LoadPackagesAsync(CancellationToken.None);
_ = agent.AcceptClientsAsync(CancellationToken.None);

app.Run();
