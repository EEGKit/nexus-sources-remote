using Nexus.DataModel;
using Nexus.Extensibility;
using System.Net;

namespace Nexus.Extensions
{
    [ExtensionDescription(
        "Provides access to remote databases",
        "https://github.com/Nexusforge/nexus-sources-remote",
        "https://github.com/Nexusforge/nexus-sources-remote")]
    public class Remote : IDataSource, IDisposable
    {
        #region Fields

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
            CancellationToken cancellationToken)
        {
            Context = context;

            // command
            if (!Context.SourceConfiguration.TryGetValue("command", out var command))
                throw new KeyNotFoundException("The command parameter must be provided.");

            // listen-address
            if (!Context.SourceConfiguration.TryGetValue("listen-address", out var listenAddressString))
                throw new KeyNotFoundException("The listen-address parameter must be provided.");

            if (!IPAddress.TryParse(listenAddressString, out var listenAddress))
                throw new KeyNotFoundException("The listen-address parameter is not a valid IP-Address.");

            // listen-port
            if (!Context.SourceConfiguration.TryGetValue("listen-port", out var listenPortString))
                throw new KeyNotFoundException("The listen-port parameter must be provided.");

            if (!ushort.TryParse(listenPortString, out var listenPort))
                throw new KeyNotFoundException("The listen-port parameter is not a valid port.");

            // arguments
            var arguments = Context.SourceConfiguration.ContainsKey("arguments")
                ? Context.SourceConfiguration["arguments"]
                : string.Empty;

            // arguments
            var environmentVariablesRaw = Context.SourceConfiguration.ContainsKey("environment-variables")
                ? Context.SourceConfiguration["environment-variables"]
                : string.Empty;

            var environmentVariables = environmentVariablesRaw.Split(";").Select(entry => 
            {
                var parts = entry.Split("=");
                var key = parts[0];
                var value = parts[1];

                return (key, value);
            }).ToDictionary(entry => entry.key, entry => entry.value);

            var timeoutTokenSource = GetTimeoutTokenSource(TimeSpan.FromSeconds(10));

            _communicator = new RemoteCommunicator(command, arguments, environmentVariables, listenAddress, listenPort, Context.Logger);
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
