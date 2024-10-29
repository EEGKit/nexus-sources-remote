using System.Text.Json;

namespace Nexus.Core.V1;

/// <summary>
/// A package reference.
/// </summary>
/// <param name="Provider">The provider which loads the package.</param>
/// <param name="Configuration">The configuration of the package reference.</param>
public record PackageReference(
    string Provider,
    Dictionary<string, string> Configuration
);

/// <summary>
/// An extension description.
/// </summary>
/// <param name="Type">The extension type.</param>
/// <param name="Version">The extension version.</param>
/// <param name="Description">A nullable description.</param>
/// <param name="ProjectUrl">A nullable project website URL.</param>
/// <param name="RepositoryUrl">A nullable source repository URL.</param>
/// <param name="AdditionalInformation">Additional information about the extension.</param>
public record ExtensionDescription(
    string Type,
    string Version,
    string? Description,
    string? ProjectUrl,
    string? RepositoryUrl,
    IReadOnlyDictionary<string, JsonElement>? AdditionalInformation);