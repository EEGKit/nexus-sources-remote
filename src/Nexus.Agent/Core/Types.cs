using System.Runtime.InteropServices;
using System.Text.Json;

namespace Nexus.Core;

internal record PathsOptions
{
    public const string Section = "Paths";

    public string Config { get; set; } = Path.Combine(PlatformSpecificRoot, "config");

    public string Packages { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexus", "packages");
    // GetGlobalPackagesFolder: https://github.com/NuGet/NuGet.Client/blob/0fc58e13683565e7bdf30e706d49e58fc497bbed/src/NuGet.Core/NuGet.Configuration/Utility/SettingsUtility.cs#L225-L254
    // GetFolderPath: https://github.com/NuGet/NuGet.Client/blob/1d75910076b2ecfbe5f142227cfb4fb45c093a1e/src/NuGet.Core/NuGet.Common/PathUtil/NuGetEnvironment.cs#L54-L57

    #region Support

    private static string PlatformSpecificRoot { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nexus.Agent")
        : "/var/lib/nexus-agent";

    #endregion
}