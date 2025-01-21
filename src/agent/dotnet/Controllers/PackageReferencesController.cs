using Microsoft.AspNetCore.Mvc;
using Apollo3zehn.PackageManagement.Services;
using Apollo3zehn.PackageManagement;

namespace Nexus.Agent.Controllers;

/// <summary>
/// Provides access to package references.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class PackageReferencesController(
    IPackageService packageService) : ControllerBase
{
    // GET      /api/packagereferences
    // POST     /api/packagereferences
    // DELETE   /api/packagereferences/{id}

    private readonly IPackageService _packageService = packageService;
    
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
}
