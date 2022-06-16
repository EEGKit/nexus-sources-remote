using Microsoft.Extensions.Logging.Abstractions;
using Nexus.DataModel;
using Nexus.Extensibility;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Nexus.Sources.Tests
{
    public class SetupDockerPythonTests
    {
#if LINUX
        [Fact]
#endif
        public async Task CanReadFullDay()
        {
            var dataSource = new Remote() as IDataSource;
            var command = "python main.py nexus-main {remote-port}";
            var context = CreateContext(command);

            await dataSource.SetContextAsync(context, NullLogger.Instance, CancellationToken.None);

            var catalog = await dataSource.GetCatalogAsync("/A/B/C", CancellationToken.None);
            var resource = catalog.Resources!.First();
            var representation = resource.Representations!.First();

            var catalogItem = new CatalogItem(
                catalog with { Resources = default! }, 
                resource with { Representations = default! }, 
                representation);

            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 01, 0, 0, 10, DateTimeKind.Utc);
            var (data, status) = ExtensibilityUtilities.CreateBuffers(representation, begin, end);

            var length = 10;
            var expectedData = new double[length];
            var expectedStatus = new byte[length];

            for (int i = 0; i < length; i++)
            {
                expectedData[i] = i;
            }

            expectedStatus.AsSpan().Fill(1);

            var request = new ReadRequest(catalogItem, data, status);
            await dataSource.ReadAsync(begin, end, new ReadRequest[] { request }, default!, new Progress<double>(), CancellationToken.None);
            var doubleData = new CastMemoryManager<byte, double>(data).Memory;

            Assert.True(expectedData.SequenceEqual(doubleData.ToArray()));
            Assert.True(expectedStatus.SequenceEqual(status.ToArray()));
        }

        private DataSourceContext CreateContext(string command)
        {
            return new DataSourceContext(
                ResourceLocator: new Uri("file:///" + Path.Combine(Directory.GetCurrentDirectory(), "TESTDATA")),
                SystemConfiguration: new JsonObject()
                {
                    ["remote-templates"] = new JsonObject()
                    {
                        ["local"] = "ssh root@nexus-python bash run-python-root.sh {git-url} {command}",
                    }
                }.Deserialize<JsonElement>(),
                SourceConfiguration: new JsonObject()
                {
                    ["listen-address"] = "0.0.0.0",
                    ["template"] = "local",
                    ["command"] = command,
                    ["git-url"] = "https://github.com/Nexusforge/nexus-remoting-sample",                    
                    ["environment-variables"] = new JsonObject()
                    {
                        ["PYTHONPATH"] = $"{Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "src", "remoting", "python-remoting")}"
                    }
                }.Deserialize<JsonElement>(),
                RequestConfiguration: default
            );
        }
    }
}