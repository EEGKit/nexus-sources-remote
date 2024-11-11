using System.Runtime.InteropServices;

namespace Nexus.Core;

internal abstract record NexusOptionsBase()
{
    // for testing only
    public string? BlindSample { get; set; }

    internal static IConfiguration BuildConfiguration()
    {
        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        var builder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json");

        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            builder
                .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true);
        }

        builder.AddEnvironmentVariables(prefix: "NEXUSAGENT_");

        return builder.Build();
    }
}

internal record PathsOptions : IPackageManagementPathsOptions
{
    public const string Section = "Paths";

    public string Config { get; set; } = Path.Combine(PlatformSpecificRoot, "config");

    public string Packages { get; set; } = Path.Combine(PlatformSpecificRoot, "packages");

    #region Support

    private static string PlatformSpecificRoot { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nexus-agent")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "nexus-agent");

    #endregion
}