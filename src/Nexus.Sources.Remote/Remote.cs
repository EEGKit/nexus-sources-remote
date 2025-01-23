using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;
using System.Buffers;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexus.Sources;

[ExtensionDescription(
    "Provides access to remote databases",
    "https://github.com/nexus-main/nexus-sources-remote",
    "https://github.com/nexus-main/nexus-sources-remote")] 
public partial class Remote : IDataSource, IDisposable
{
    private const int DEFAULT_AGENT_PORT = 56145;

    private ReadDataHandler? _readData;

    private static readonly int API_LEVEL = 1;

    private RemoteCommunicator _communicator = default!;
    
    private IJsonRpcServer _rpcServer = default!;

    /* Possible features to be implemented for this data source:
     * 
     * Transports: 
     *      - anonymous pipes (done)
     *      - named pipes client
     *      - tcp client
     *      - shared memory
     *      - ...
     *      
     * Protocols:
     *      - JsonRpc + binary data stream (done)
     *      - 0mq
     *      - messagepack
     *      - gRPC
     *      - ...
     */

    private DataSourceContext Context { get; set; } = default!;

    public async Task SetContextAsync(
        DataSourceContext context,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        Context = context;

        // Host and port
        if (context.ResourceLocator is null || context.ResourceLocator.Scheme != "tcp")
            throw new ArgumentException("The resource locator parameter URI must be set with the 'tcp' scheme.");

        var host = context.ResourceLocator.Host;
        var port = context.ResourceLocator.Port;

        if (port == -1)
            port = DEFAULT_AGENT_PORT;

        // Type
        var type = Context.SourceConfiguration?.GetStringValue("type") ?? throw new Exception("The data source type is missing.");

        // Resource locator
        var resourceLocatorString = Context.SourceConfiguration?.GetStringValue("resourceLocator");

        if (!Uri.TryCreate(resourceLocatorString, UriKind.Absolute, out var resourceLocator))
            throw new ArgumentException("The resource locator parameter is not a valid URI.");

        // Source configuration
        var sourceConfigurationJsonElement = Context.SourceConfiguration?.GetValueOrDefault("sourceConfiguration");

        var sourceConfiguration = sourceConfigurationJsonElement.HasValue && sourceConfigurationJsonElement.Value.ValueKind == JsonValueKind.Object

            ? JsonSerializer
                .Deserialize<IReadOnlyDictionary<string, JsonElement>>(sourceConfigurationJsonElement.Value)

            : JsonSerializer
                .Deserialize<IReadOnlyDictionary<string, JsonElement>>("{}");

        // Remote communicator
        _communicator = new RemoteCommunicator(
            host,
            port,
            HandleReadDataAsync,
            logger
        );

        var timeoutTokenSource = GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
        cancellationToken.Register(timeoutTokenSource.Cancel);

        _rpcServer = await _communicator.ConnectAsync(timeoutTokenSource.Token);

        var apiVersion = (await _rpcServer.GetApiVersionAsync(timeoutTokenSource.Token)).ApiVersion;

        if (apiVersion < 1 || apiVersion > API_LEVEL)
            throw new Exception($"The API level '{apiVersion}' is not supported.");

        // Set context
        logger.LogTrace("Set context to remote client");

        var subContext = new DataSourceContext(
            resourceLocator,
            context.SystemConfiguration,
            sourceConfiguration,
            context.RequestConfiguration
        );

        await _rpcServer
            .SetContextAsync(type, subContext, timeoutTokenSource.Token);
    }

    public async Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var timeoutTokenSource = GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
        cancellationToken.Register(timeoutTokenSource.Cancel);

        var response = await _rpcServer
            .GetCatalogRegistrationsAsync(path, timeoutTokenSource.Token);

        return response.Registrations;
    }

    public async Task<ResourceCatalog> EnrichCatalogAsync(
        ResourceCatalog catalog,
        CancellationToken cancellationToken)
    {
        var timeoutTokenSource = GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
        cancellationToken.Register(timeoutTokenSource.Cancel);

        var response = await _rpcServer
            .EnrichCatalogAsync(catalog, timeoutTokenSource.Token);

        return response.Catalog;
    }

    public async Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(
        string catalogId,
        CancellationToken cancellationToken)
    {
        var timeoutTokenSource = GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
        cancellationToken.Register(timeoutTokenSource.Cancel);

        var response = await _rpcServer
            .GetTimeRangeAsync(catalogId, timeoutTokenSource.Token);

        var begin = response.Begin.ToUniversalTime();
        var end = response.End.ToUniversalTime();

        return (begin, end);
    }

    public async Task<double> GetAvailabilityAsync(
        string catalogId,
        DateTime begin,
        DateTime end,
        CancellationToken cancellationToken)
    {
        var timeoutTokenSource = GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
        cancellationToken.Register(timeoutTokenSource.Cancel);

        var response = await _rpcServer
            .GetAvailabilityAsync(catalogId, begin, end, timeoutTokenSource.Token);

        return response.Availability;
    }

    public async Task ReadAsync(
        DateTime begin,
        DateTime end,
        ReadRequest[] requests,
        ReadDataHandler readData,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        _readData = readData;

        try
        {
            var counter = 0.0;

            foreach (var (originalResourceName, catalogItem, data, status) in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var timeoutTokenSource = GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
                cancellationToken.Register(timeoutTokenSource.Cancel);

                var elementCount = data.Length / catalogItem.Representation.ElementSize;

                await _rpcServer
                    .ReadSingleAsync(begin, end, originalResourceName, catalogItem, timeoutTokenSource.Token);

                await _communicator.ReadRawAsync(data, timeoutTokenSource.Token);
                await _communicator.ReadRawAsync(status, timeoutTokenSource.Token);

                progress.Report(++counter / requests.Length);
            }
        }
        finally
        {
            _readData = null;
        }
    }

    // copy from Nexus -> DataModelUtilities
    private static readonly Regex _resourcePathEvaluator = MyRegex();

    private static readonly MethodInfo _toSamplePeriodMethodInfo = typeof(DataModelExtensions)
        .GetMethod("ToSamplePeriod", BindingFlags.Static | BindingFlags.NonPublic) ?? throw new Exception("Unable to locate ToSamplePeriod method.");

    private async Task HandleReadDataAsync(string resourcePath, DateTime begin, DateTime end)
    {
        // copy of _readData handler
        var localReadData = _readData ?? throw new InvalidOperationException("Unable to read data without previous invocation of the ReadAsync method.");

        // timeout token source
        var timeoutTokenSource = GetTimeoutTokenSource(TimeSpan.FromMinutes(1));

        // find sample period
        var match = _resourcePathEvaluator.Match(resourcePath);

        if (!match.Success)
            throw new Exception("Invalid resource path");

        var samplePeriod = (TimeSpan)_toSamplePeriodMethodInfo.Invoke(null, [
            match.Groups["sample_period"].Value
        ])!;

        // find buffer length and rent buffer
        var length = (int)((end - begin).Ticks / samplePeriod.Ticks);

        using var memoryOwner = MemoryPool<double>.Shared.Rent(length);
        var buffer = memoryOwner.Memory[..length];

        // read data
        await localReadData(resourcePath, begin, end, buffer, timeoutTokenSource.Token);
        var byteBuffer = new CastMemoryManager<double, byte>(buffer).Memory;

        // write to communicator
        await _communicator.WriteRawAsync(byteBuffer, timeoutTokenSource.Token);
    }

    private static CancellationTokenSource GetTimeoutTokenSource(TimeSpan timeout)
    {
        var timeoutToken = new CancellationTokenSource();
        timeoutToken.CancelAfter(timeout);

        return timeoutToken;
    }

    #region IDisposable

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _communicator?.Dispose();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    [GeneratedRegex("{(.*?)}")]
    private static partial Regex CommandRegex();
    [GeneratedRegex(@"^(?'catalog'.*)\/(?'resource'.*)\/(?'sample_period'[0-9]+_[a-zA-Z]+)(?:_(?'kind'[^\(#\s]+))?(?:\((?'parameters'.*)\))?(?:#(?'fragment'.*))?$", RegexOptions.Compiled)]
    private static partial Regex MyRegex();

    #endregion
}
