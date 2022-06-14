using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Nexus.Sources
{
    [ExtensionDescription(
        "Provides access to remote databases",
        "https://github.com/Nexusforge/nexus-sources-remote",
        "https://github.com/Nexusforge/nexus-sources-remote")]
    public class Remote : IDataSource, IDisposable
    {
        #region Fields

        private ReadDataHandler? _readData;
        private static int API_LEVEL = 1;
        private RemoteCommunicator _communicator = default!;
        private IJsonRpcServer _rpcServer = default!;

        #endregion

        #region Properties

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

        #endregion

        #region Methods

        public async Task SetContextAsync(
            DataSourceContext context, 
            ILogger logger,
            CancellationToken cancellationToken)
        {
            Context = context;

            // command
            if (!TryGetStringValue("command", out var command))
                throw new KeyNotFoundException("The command parameter must be provided.");

            // listen-address
            if (!TryGetStringValue("listen-address", out var listenAddressString))
                throw new KeyNotFoundException("The listen-address parameter must be provided.");

            if (!IPAddress.TryParse(listenAddressString, out var listenAddress))
                throw new KeyNotFoundException("The listen-address parameter is not a valid IP-Address.");

            // listen-port
            if (!TryGetStringValue("listen-port", out var listenPortString))
                throw new KeyNotFoundException("The listen-port parameter must be provided.");

            if (!ushort.TryParse(listenPortString, out var listenPort))
                throw new KeyNotFoundException("The listen-port parameter is not a valid port.");

            // arguments
            if (!TryGetStringValue("arguments", out var arguments))
                arguments = string.Empty;

            // environment variables
            var requestConfiguration = Context.SourceConfiguration!.Value;
            var environmentVariables = new Dictionary<string, string>();

            if (requestConfiguration.ValueKind == JsonValueKind.Object &&
                requestConfiguration.TryGetProperty("environment-variables", out var propertyValue) &&
                propertyValue.ValueKind == JsonValueKind.Object)
            {
                var environmentVariablesRaw = JsonSerializer
                    .Deserialize<Dictionary<string, JsonElement>>(propertyValue);
                    
                if (environmentVariablesRaw is not null)
                    environmentVariables = environmentVariablesRaw
                        .Where(entry => entry.Value.ValueKind == JsonValueKind.String)
                        .ToDictionary(entry => entry.Key, entry => entry.Value.GetString() ?? "");
            }

            //
            var timeoutTokenSource = GetTimeoutTokenSource(TimeSpan.FromSeconds(10));

            _communicator = new RemoteCommunicator(
                command,
                arguments, 
                environmentVariables, 
                listenAddress,
                listenPort,
                HandleReadDataAsync,
                logger);

            _rpcServer = await _communicator.ConnectAsync(timeoutTokenSource.Token);

            var apiVersion = (await _rpcServer.GetApiVersionAsync(timeoutTokenSource.Token)).ApiVersion;

            if (apiVersion < 1 || apiVersion > Remote.API_LEVEL)
                throw new Exception($"The API level '{apiVersion}' is not supported.");

            await _rpcServer
                .SetContextAsync(context, timeoutTokenSource.Token);
        }

        public async Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(
            string path,
            CancellationToken cancellationToken)
        {
            var timeoutTokenSource = GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

            var response = await _rpcServer
                .GetCatalogRegistrationsAsync(path, timeoutTokenSource.Token);

            return response.Registrations;
        }

        public async Task<ResourceCatalog> GetCatalogAsync(
            string catalogId, 
            CancellationToken cancellationToken)
        {
            var timeoutTokenSource = GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

            var response = await _rpcServer
                .GetCatalogAsync(catalogId, timeoutTokenSource.Token);

            return response.Catalog;
        }

        public async Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(
            string catalogId, 
            CancellationToken cancellationToken)
        {
            var timeoutTokenSource = GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

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
            cancellationToken.Register(() => timeoutTokenSource.Cancel());

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

                foreach (var (catalogItem, data, status) in requests)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var timeoutTokenSource = GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
                    cancellationToken.Register(() => timeoutTokenSource.Cancel());

                    var elementCount = data.Length / catalogItem.Representation.ElementSize;

                    await _rpcServer
                        .ReadSingleAsync(begin, end, catalogItem, timeoutTokenSource.Token);

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

        async Task HandleReadDataAsync(string resourcePath, DateTime begin, DateTime end)
        {
            var localReadData = _readData;

            if (localReadData is null)
                throw new InvalidOperationException("Unable to read data without previous invocation of the ReadAsync method.");

            var timeoutTokenSource = GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            var data = await localReadData(resourcePath, begin, end, timeoutTokenSource.Token);
            var byteData = new CastMemoryManager<double, byte>(MemoryMarshal.AsMemory(data)).Memory;

            await _communicator.WriteRawAsync(byteData, timeoutTokenSource.Token);
        }

        private bool TryGetStringValue(string propertyName, [NotNullWhen(returnValue: true)] out string? value)
        {
            value = default;
            var requestConfiguration = Context.SourceConfiguration!.Value;

            if (requestConfiguration.ValueKind == JsonValueKind.Object &&
                requestConfiguration.TryGetProperty(propertyName, out var propertyValue) &&
                propertyValue.ValueKind == JsonValueKind.String)
                value = propertyValue.GetString();

            return value != default;
        }

        private CancellationTokenSource GetTimeoutTokenSource(TimeSpan timeout)
        {
            var timeoutToken = new CancellationTokenSource();
            timeoutToken.CancelAfter(timeout);

            return timeoutToken;
        }

        #endregion

        #region IDisposable

        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _communicator?.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
