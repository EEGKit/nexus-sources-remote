using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus.DataModel;
using Nexus.Extensibility;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Nexus.Sources.Tests
{
    [Trait("TestCategory", "local")]
    public class RemoteTests
    {
        [Theory]
        [InlineData("dotnet run --project remote.csproj localhost {remote-port}")]
        [InlineData("python remote.py localhost {remote-port}")]
#if LINUX
        [InlineData("bash/remote.sh localhost {remote-port}")]
#endif
        public async Task ProvidesCatalog(string command)
        {
            // arrange
            var dataSource = new Remote() as IDataSource;
            var context = CreateContext(command);

            await dataSource.SetContextAsync(context, NullLogger.Instance, CancellationToken.None);

            // act
            var actual = await dataSource.GetCatalogAsync("/A/B/C", CancellationToken.None);

            // assert
            var actualProperties1 = actual.Properties;
            var actualIds = actual.Resources!.Select(resource => resource.Id).ToList();
            var actualUnits = actual.Resources!.Select(resource => GetPropertyOrDefault(resource.Properties, "unit")).ToList();
            var actualGroups = actual.Resources!.SelectMany(resource => GetArrayOrDefault(resource.Properties, "groups"));
            var actualDataTypes = actual.Resources!.SelectMany(resource => resource.Representations!.Select(representation => representation.DataType)).ToList();

            var expectedProperties1 = new Dictionary<string, string>() { ["a"] = "b" };
            var expectedIds = new List<string>() { "resource1", "resource2" };
            var expectedUnits = new List<string>() { "°C", "bar" };
            var expectedDataTypes = new List<NexusDataType>() { NexusDataType.INT64, NexusDataType.FLOAT64 };
            var expectedGroups = new List<string>() { "group1", "group2" };

            Assert.True(JsonSerializer.Serialize(actualProperties1) == JsonSerializer.Serialize(expectedProperties1));
            Assert.True(expectedIds.SequenceEqual(actualIds));
            Assert.True(expectedUnits.SequenceEqual(actualUnits));
            Assert.True(expectedGroups.SequenceEqual(actualGroups));
            Assert.True(expectedDataTypes.SequenceEqual(actualDataTypes));

            string? GetPropertyOrDefault(JsonElement? element, string propertyName)
            {
                if (!element.HasValue)
                    return default;

                if (element.Value.TryGetProperty(propertyName, out var result))
                    return result.GetString();

                else
                    return default;
            }

            string[] GetArrayOrDefault(JsonElement? element, string propertyName)
            {
                if (!element.HasValue)
                    return new string[0];

                if (element.Value.TryGetProperty(propertyName, out var result))
                    return result.EnumerateArray().Select(current => current.GetString()!).ToArray();

                else
                    return new string[0];
            }
        }

        [Theory]
        [InlineData("dotnet run --project remote.csproj localhost {remote-port}")]
        [InlineData("python remote.py localhost {remote-port}")]
#if LINUX
        [InlineData("bash/remote.sh localhost {remote-port}")]
#endif
        public async Task CanProvideTimeRange(string command)
        {
            var dataSource = new Remote() as IDataSource;
            var context = CreateContext(command);

            var expectedBegin = new DateTime(2019, 12, 31, 12, 00, 00, DateTimeKind.Utc);
            var expectedEnd = new DateTime(2020, 01, 02, 09, 50, 00, DateTimeKind.Utc);

            await dataSource.SetContextAsync(context, NullLogger.Instance, CancellationToken.None);

            var actual = await dataSource.GetTimeRangeAsync("/A/B/C", CancellationToken.None);

            Assert.Equal(expectedBegin, actual.Begin);
            Assert.Equal(expectedEnd, actual.End);
        }

        [Theory]
        [InlineData("dotnet run --project remote.csproj localhost {remote-port}")]
        [InlineData("python remote.py localhost {remote-port}")]
#if LINUX
        [InlineData("bash/remote.sh localhost {remote-port}")]
#endif
        public async Task CanProvideAvailability(string command)
        {
            var dataSource = new Remote() as IDataSource;
            var context = CreateContext(command);

            await dataSource.SetContextAsync(context, NullLogger.Instance, CancellationToken.None);

            var begin = new DateTime(2020, 01, 02, 00, 00, 00, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 03, 00, 00, 00, DateTimeKind.Utc);
            var actual = await dataSource.GetAvailabilityAsync("/A/B/C", begin, end, CancellationToken.None);

            Assert.Equal(2 / 144.0, actual, precision: 4);
        }

        [Theory]
        [InlineData("dotnet run --project remote.csproj localhost {remote-port}", true)]
        [InlineData("python remote.py localhost {remote-port}", true)]
#if LINUX
        [InlineData("bash/remote.sh localhost {remote-port}", false)]
#endif
        public async Task CanReadFullDay(string command, bool complexData)
        {
            var dataSource = new Remote() as IDataSource;
            var context = CreateContext(command);

            await dataSource.SetContextAsync(context, NullLogger.Instance, CancellationToken.None);

            var catalog = await dataSource.GetCatalogAsync("/A/B/C", CancellationToken.None);
            var resource = catalog.Resources!.First();
            var representation = resource.Representations!.First();

            var catalogItem = new CatalogItem(
                catalog with { Resources = default! }, 
                resource with { Representations = default! }, 
                representation);

            var begin = new DateTime(2019, 12, 31, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 03, 0, 0, 0, DateTimeKind.Utc);
            var (data, status) = ExtensibilityUtilities.CreateBuffers(representation, begin, end);

            var length = 3 * 86400;
            var expectedData = new long[length];
            var expectedStatus = new byte[length];

            if (complexData)
            {

                void GenerateData(DateTimeOffset dateTime)
                {
                    var data = Enumerable.Range(0, 600)
                        .Select(value => dateTime.Add(TimeSpan.FromSeconds(value)).ToUnixTimeSeconds())
                        .ToArray();

                    var offset = (int)(dateTime - begin).TotalSeconds;
                    data.CopyTo(expectedData.AsSpan().Slice(offset));
                    expectedStatus.AsSpan().Slice(offset, 600).Fill(1);
                }

                GenerateData(new DateTimeOffset(2019, 12, 31, 12, 00, 0, 0, TimeSpan.Zero));
                GenerateData(new DateTimeOffset(2019, 12, 31, 12, 20, 0, 0, TimeSpan.Zero));
                GenerateData(new DateTimeOffset(2020, 01, 01, 00, 00, 0, 0, TimeSpan.Zero));
                GenerateData(new DateTimeOffset(2020, 01, 02, 09, 40, 0, 0, TimeSpan.Zero));
                GenerateData(new DateTimeOffset(2020, 01, 02, 09, 50, 0, 0, TimeSpan.Zero));
            }
            else
            {
                MemoryMarshal.AsBytes(expectedData.AsSpan()).Fill((byte)'d');
                expectedStatus.AsSpan().Fill((byte)'s');
            }

            var request = new ReadRequest(catalogItem, data, status);
            await dataSource.ReadAsync(begin, end, new ReadRequest[] { request }, default!, new Progress<double>(), CancellationToken.None);
            var longData = new CastMemoryManager<byte, long>(data).Memory;

            Assert.True(expectedData.SequenceEqual(longData.ToArray()));
            Assert.True(expectedStatus.SequenceEqual(status.ToArray()));
        }

        [Theory]
        [InlineData("dotnet run --project remote.csproj localhost {remote-port}")]
        [InlineData("python remote.py localhost {remote-port}")]
#if LINUX
        [InlineData("bash/remote.sh localhost {remote-port}")]
#endif
        public async Task CanLog(string command)
        {
            var loggerMock = new Mock<ILogger>();
            var dataSource = new Remote() as IDataSource;
            var context = CreateContext(command);

            await dataSource.SetContextAsync(context, loggerMock.Object, CancellationToken.None);

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(logLevel => logLevel == LogLevel.Information),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((message, _) => message.ToString() == "Logging works!"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)
                ),
                Times.Once
            );
        }

        [Theory]
        [InlineData("dotnet run --project remote.csproj localhost {remote-port}")]
        [InlineData("python remote.py localhost {remote-port}")]
        public async Task CanReadDataHandler(string command)
        {
            var dataSource = new Remote() as IDataSource;
            var context = CreateContext(command);

            await dataSource.SetContextAsync(context, NullLogger.Instance, CancellationToken.None);

            var catalog = await dataSource.GetCatalogAsync("/D/E/F", CancellationToken.None);
            var resource = catalog.Resources!.First();
            var representation = resource.Representations!.First();

            var catalogItem = new CatalogItem(
                catalog with { Resources = default! },
                resource with { Representations = default! },
                representation);

            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 01, 0, 1, 0, DateTimeKind.Utc);
            var (data, status) = ExtensibilityUtilities.CreateBuffers(representation, begin, end);

            var length = 60;

            var expectedData = Enumerable
                .Range(0, length)
                .Select(value => (double)value * 2)
                .ToArray();

            var expectedStatus = Enumerable
                .Range(0, length)
                .Select(value => (byte)1)
                .ToArray();

            Task<ReadOnlyMemory<double>> HandleReadDataAsync(string resourcePath, DateTime begin, DateTime end, CancellationToken cancellationToken)
            {
                ReadOnlyMemory<double> data = Enumerable
                    .Range(0, length)
                    .Select(value => (double)value)
                    .ToArray();

                return Task.FromResult(data);
            }

            var request = new ReadRequest(catalogItem, data, status);
            await dataSource.ReadAsync(begin, end, new ReadRequest[] { request }, HandleReadDataAsync, new Progress<double>(), CancellationToken.None);
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
                        ["local"] = "{command}",
                    }
                }.Deserialize<JsonElement>(),
                SourceConfiguration: new JsonObject()
                {
                    ["listen-address"] = "127.0.0.1",
                    ["listen-port-min"] = "63000",
                    ["template"] = "local",
                    ["command"] = command,
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