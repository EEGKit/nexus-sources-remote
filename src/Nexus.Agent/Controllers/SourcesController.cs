// MIT License
// Copyright (c) [2024] [nexus-main]

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core.V1;
using Nexus.Extensibility;
using Nexus.Services;
using System.Reflection;

namespace Nexus.Controllers;

/// <summary>
/// Provides access to extensions.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class SourcesController(
    IExtensionHive extensionHive
) : ControllerBase
{
    // GET      /api/sources/descriptions

    private readonly IExtensionHive _extensionHive = extensionHive;

    /// <summary>
    /// Gets the list of source descriptions.
    /// </summary>
    [HttpGet("descriptions")]
    public List<ExtensionDescription> GetDescriptions()
    {
        var result = GetExtensionDescriptions(_extensionHive.GetExtensions<IDataSource>());
        return result;
    }
    
    private static List<ExtensionDescription> GetExtensionDescriptions(
        IEnumerable<Type> extensions)
    {
        return extensions.Select(type =>
        {
            var version = type.Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion;

            var attribute = type
                .GetCustomAttribute<ExtensionDescriptionAttribute>(inherit: false);

            if (attribute is null)
                return new ExtensionDescription(type.FullName!, version, default, default, default, default);

            else
                return new ExtensionDescription(type.FullName!, version, attribute.Description, attribute.ProjectUrl, attribute.RepositoryUrl, default);
        })
        .ToList();
    }
}
