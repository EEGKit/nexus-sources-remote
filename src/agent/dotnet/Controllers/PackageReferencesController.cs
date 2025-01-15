// MIT License
// Copyright (c) [2024] [nexus-main]

using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Nexus.PackageManagement.Services;
using Nexus.PackageManagement;

namespace Nexus.Controllers;

/// <summary>
/// Provides access to package references.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class PackageReferencesController(
    IPackageService packageService,
    IExtensionHive extensionHive) : ControllerBase
{
    // GET      /api/packagereferences
    // POST     /api/packagereferences
    // DELETE   /api/packagereferences/{id}
    // GET      /api/packagereferences/{id}/versions

    private readonly IPackageService _packageService = packageService;
    
    private readonly IExtensionHive _extensionHive = extensionHive;

    /// <summary>
    /// Gets the list of package references.
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    public Task<IReadOnlyDictionary<Guid, PackageReference>> GetAsync()
    {
        return _packageService.GetAllAsync();
    }

    /// <summary>
    /// Creates a package reference.
    /// </summary>
    /// <param name="packageReference">The package reference to create.</param>
    [HttpPost]
    public Task<Guid> CreateAsync(
        PackageReference packageReference)
    {
        return _packageService.PutAsync(packageReference);
    }

    /// <summary>
    /// Deletes a package reference.
    /// </summary>
    /// <param name="id">The ID of the package reference.</param>
    [HttpDelete("{id}")]
    public Task DeleteAsync(
        Guid id)
    {
        return _packageService.DeleteAsync(id);
    }

    /// <summary>
    /// Gets package versions.
    /// </summary>
    /// <param name="id">The ID of the package reference.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    [HttpGet("{id}/versions")]
    public async Task<ActionResult<string[]>> GetVersionsAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var packageReferenceMap = await _packageService.GetAllAsync();

        if (!packageReferenceMap.TryGetValue(id, out var packageReference))
            return NotFound($"Unable to find package reference with ID {id}.");

        var result = await _extensionHive
            .GetVersionsAsync(packageReference, cancellationToken);

        return result;
    }
}
