using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Options;
using Nexus.Agent.Core;
using Nexus.Extensibility;

var builder = WebApplication.CreateBuilder();

var configuration = NexusAgentOptions.BuildConfiguration();
builder.Configuration.AddConfiguration(configuration);

builder.Services.AddAgentOpenApi();
builder.Services.AddPackageManagement();
builder.Services.AddExtensionHive<IDataSource>();

builder.Services.AddSingleton<AgentService>();

builder.Services.AddSingleton<IPackageManagementPathsOptions>(
    serviceProvider => serviceProvider.GetRequiredService<IOptions<PathsOptions>>().Value);

builder.Services.Configure<SystemOptions>(configuration.GetSection(SystemOptions.Section));
builder.Services.Configure<PathsOptions>(configuration.GetSection(PathsOptions.Section));

var app = builder.Build();

var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
app.UseAgentOpenApi(provider);

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/api"));

var pathsOptions = app.Services.GetRequiredService<IOptions<PathsOptions>>();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Current directory: {CurrentDirectory}", Environment.CurrentDirectory);
logger.LogInformation("Loading configuration from path: {ConfigFolderPath}", pathsOptions.Value.Config);

var agent = app.Services.GetRequiredService<AgentService>();
await agent.LoadPackagesAsync(CancellationToken.None);
_ = agent.AcceptClientsAsync(CancellationToken.None);

app.Run();
