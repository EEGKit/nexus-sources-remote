using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.DataModel;
using Nexus.Extensibility;
using Xunit;

namespace Nexus.Sources.Tests;

public class AgentTests
{
    [Fact]
    public async Task CanProvideCatalog()
    {
        // Arrange
        var dataSource = new Remote() as IDataSource;

        var context = new DataSourceContext(
            ResourceLocator: new Uri("file:///" + Path.Combine(Directory.GetCurrentDirectory(), "TESTDATA")),
            SystemConfiguration: default,
            SourceConfiguration: new Dictionary<string, JsonElement>()
            {
                ["type"] = JsonSerializer.SerializeToElement("Nexus.Sources.Famos")
            },
            RequestConfiguration: default
        );

        await dataSource.SetContextAsync(context, NullLogger.Instance, CancellationToken.None);

        // Act
        var actual = await dataSource.EnrichCatalogAsync(new ResourceCatalog("/A/B/C"), CancellationToken.None);

        // Assert
        var b = 1;
    }
}