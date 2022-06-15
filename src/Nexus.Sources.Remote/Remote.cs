using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

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

            // mode
            var mode = GetStringValueOrDefault("mode", "tcp", Context.SourceConfiguration);

            if (mode != "tcp")
                throw new NotSupportedException($"The mode {mode} is not supported.");

            // listen-address
            var listenAddressString = GetStringValueOrDefault("listen-address", "0.0.0.0", Context.SourceConfiguration);

            if (!IPAddress.TryParse(listenAddressString, out var listenAddress))
                throw new KeyNotFoundException("The listen-address parameter is not a valid IP-Address.");

            // listen-port
            var defaultMin = 49152;
            var listenPortMin = GetIntValueOrDefault("listen-port-min", defaultMin, Context.SourceConfiguration);

            if (!(1 <= listenPortMin && listenPortMin < 65536))
                throw new KeyNotFoundException("The listen-port-min parameter is invalid.");

            var defaultMax = 65536;
            var listenPortMax = GetIntValueOrDefault("listen-port-max", defaultMax, Context.SourceConfiguration);

            if (!(1 <= listenPortMin && listenPortMin < 65536))
                throw new KeyNotFoundException("The listen-port-max parameter is invalid.");

            // template
            if (!TryGetStringValue("template", Context.SourceConfiguration, out var templateId))
                throw new KeyNotFoundException("The template parameter must be provided.");

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

            // Build command
            var actualCommand = BuildCommand(templateId);

            //
            var timeoutTokenSource = GetTimeoutTokenSource(TimeSpan.FromSeconds(10));

            _communicator = new RemoteCommunicator(
                actualCommand,
                environmentVariables, 
                listenAddress,
                listenPortMin,
                listenPortMax,
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

        private string BuildCommand(string templateId)
        {
            var template = default(string);

            if (Context.SystemConfiguration is not null &&
                Context.SystemConfiguration.Value.ValueKind == JsonValueKind.Object &&
                Context.SystemConfiguration.Value.TryGetProperty("remote-templates", out var templatesProperty) &&
                templatesProperty.ValueKind == JsonValueKind.Object &&
                templatesProperty.TryGetProperty(templateId, out var templateProperty) &&
                templateProperty.ValueKind == JsonValueKind.String)
            {
                template = templateProperty.GetString();
            }

            if (template is null)
                throw new Exception($"The template {templateId} does not exist.");

            var command = Regex.Replace(template, "{(.*)}", match => 
            {
                var parameterKey = match.Groups[1].Value;

                if (!TryGetStringValue(parameterKey, Context.SourceConfiguration, out var parameterValue))
                    throw new Exception($"The {parameterKey} parameter must be provided.");

                return parameterValue;
            });

            return command;
        }

        private async Task HandleReadDataAsync(string resourcePath, DateTime begin, DateTime end)
        {
            var localReadData = _readData;

            if (localReadData is null)
                throw new InvalidOperationException("Unable to read data without previous invocation of the ReadAsync method.");

            var timeoutTokenSource = GetTimeoutTokenSource(TimeSpan.FromMinutes(1));
            var data = await localReadData(resourcePath, begin, end, timeoutTokenSource.Token);
            var byteData = new CastMemoryManager<double, byte>(MemoryMarshal.AsMemory(data)).Memory;

            await _communicator.WriteRawAsync(byteData, timeoutTokenSource.Token);
        }

        private bool TryGetStringValue(string propertyName, JsonElement? configuration, [NotNullWhen(returnValue: true)] out string? value)
        {
            value = default;

            if (configuration is null)
                return false;

            if (configuration.Value.ValueKind == JsonValueKind.Object &&
                configuration.Value.TryGetProperty(propertyName, out var propertyValue) &&
                propertyValue.ValueKind == JsonValueKind.String)
                value = propertyValue.GetString();

            return value != default;
        }

        private string GetStringValueOrDefault(string propertyName, string defaultValue, JsonElement? configuration)
        {
            var value = defaultValue;

            if (configuration is null)
                return value;

            if (configuration.Value.ValueKind == JsonValueKind.Object &&
                configuration.Value.TryGetProperty(propertyName, out var propertyValue) &&
                propertyValue.ValueKind == JsonValueKind.String)
                value = propertyValue.GetString() ?? value;

            return value;
        }

        private int GetIntValueOrDefault(string propertyName, int defaultValue, JsonElement? configuration)
        {
            var value = defaultValue;
            
            if (configuration is null)
                return value;

            if (configuration.Value.ValueKind == JsonValueKind.Object &&
                configuration.Value.TryGetProperty(propertyName, out var propertyValue) &&
                propertyValue.ValueKind == JsonValueKind.Number)
                value = propertyValue.GetInt32();

            return value;
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
