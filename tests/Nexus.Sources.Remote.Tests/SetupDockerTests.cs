using Microsoft.Extensions.Logging.Abstractions;
using Nexus.DataModel;
using Nexus.Extensibility;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Nexus.Sources.Tests;

[Trait("TestCategory", "docker")]
public class SetupDockerTests
{
#if LINUX
    [Theory]
    [InlineData("python", "main.py nexus-main {remote-port}", "v2.0.0-beta.24")]
    [InlineData("dotnet", "nexus-remoting-sample.csproj nexus-main {remote-port}", "v2.0.0-beta.24")]
#endif
    public async Task CanReadFullDay(string satelliteId, string command, string version)
    {
        var dataSource = new Remote() as IDataSource;
        var context = CreateContext(satelliteId, command, version);

        await dataSource.SetContextAsync(context, NullLogger.Instance, CancellationToken.None);

        var catalog = await dataSource.GetCatalogAsync("/A/B/C", CancellationToken.None);
        var resource = catalog.Resources![0];
        var representation = resource.Representations![0];

        var catalogItem = new CatalogItem(
            catalog with { Resources = default! },
            resource with { Representations = default! },
            representation,
            default);

        var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2020, 01, 01, 0, 0, 10, DateTimeKind.Utc);
        var (data, status) = ExtensibilityUtilities.CreateBuffers(representation, begin, end);

        var length = 10;
        var expectedData = new double[length];
        var expectedStatus = new byte[length];

        for (int i = 0; i < length; i++)
        {
            expectedData[i] = i * 2;
        }

        expectedStatus.AsSpan().Fill(1);

        Task ReadData(string resourcePath, DateTime begin, DateTime end, Memory<double> buffer, CancellationToken cancellationToken)
        {
            var spanBuffer = buffer.Span;

            for (int i = 0; i < length; i++)
            {
                spanBuffer[i] = i;
            }

            return Task.CompletedTask;
        }

        var request = new ReadRequest(catalogItem, data, status);

        await dataSource.ReadAsync(
            begin,
            end,
            [request],
            ReadData,
            new Progress<double>(),
            CancellationToken.None);

        var doubleData = new CastMemoryManager<byte, double>(data).Memory;

        Assert.True(expectedData.SequenceEqual(doubleData.ToArray()));
        Assert.True(expectedStatus.SequenceEqual(status.ToArray()));
    }

    private static DataSourceContext CreateContext(string satelliteId, string command, string version)
    {
        return new DataSourceContext(
            ResourceLocator: default,
            SystemConfiguration: new Dictionary<string, JsonElement>()
            {
                [typeof(Remote).FullName!] = JsonSerializer.SerializeToElement(new JsonObject()
                {
                    ["templates"] = new JsonObject()
                    {
                        ["docker"] = $"ssh root@nexus-{satelliteId} bash run.sh {{git-url}} {{git-tag}} {{command}}"
                    }
                })
            },
            SourceConfiguration: new Dictionary<string, JsonElement>()
            {
                ["listen-address"] = JsonSerializer.SerializeToElement("0.0.0.0"),
                ["template"] = JsonSerializer.SerializeToElement("docker"),
                ["command"] = JsonSerializer.SerializeToElement(command),
                ["git-url"] = JsonSerializer.SerializeToElement($"https://github.com/malstroem-labs/nexus-remoting-template-{satelliteId}"),
                ["git-tag"] = JsonSerializer.SerializeToElement(version)
            },
            RequestConfiguration: default
        );
    }
}
