// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Options;
using Nexus.Core;
using System.Diagnostics.CodeAnalysis;

namespace Nexus.Services;

internal interface IDatabaseService
{
    /* /config/packages.json */
    bool TryReadPackageReferenceMap([NotNullWhen(true)] out string? packageReferenceMap);

    Stream WritePackageReferenceMap();
}

internal class DatabaseService(IOptions<PathsOptions> pathsOptions)
    : IDatabaseService
{
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;

    private const string FILE_EXTENSION = ".json";

    private const string PACKAGES = "packages";

    /* /config/packages.json */
    public bool TryReadPackageReferenceMap([NotNullWhen(true)] out string? packageReferenceMap)
    {
        var folderPath = _pathsOptions.Config;
        var packageReferencesFilePath = Path.Combine(folderPath, PACKAGES + FILE_EXTENSION);

        packageReferenceMap = default;

        if (File.Exists(packageReferencesFilePath))
        {
            packageReferenceMap = File.ReadAllText(packageReferencesFilePath);
            return true;
        }

        return false;
    }

    public Stream WritePackageReferenceMap()
    {
        var folderPath = _pathsOptions.Config;
        var packageReferencesFilePath = Path.Combine(folderPath, PACKAGES + FILE_EXTENSION);

        Directory.CreateDirectory(folderPath);

        return File.Open(packageReferencesFilePath, FileMode.Create, FileAccess.Write);
    }
}