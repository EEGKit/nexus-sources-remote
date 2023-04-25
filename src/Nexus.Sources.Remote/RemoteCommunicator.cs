using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace Nexus.Sources
{
    internal partial class RemoteCommunicator
    {
        #region Fields

        private static readonly object _lock = new();
        private static int _nextMin = -1;
        private readonly TcpListener _tcpListener;
        private Stream _commStream = default!;
        private Stream _dataStream = default!;
        private IJsonRpcServer _rpcServer = default!;

        private readonly ILogger _logger;
        private readonly Func<string, DateTime, DateTime, Task> _readData;

        private readonly string _command;
        private readonly string _arguments;
        private readonly Dictionary<string, string> _environmentVariables;

        private Process _process = default!;

        #endregion

        #region Constructors

        public RemoteCommunicator(
            string command,
            Dictionary<string, string> environmentVariables,
            IPAddress listenAddress,
            int listenPortMin,
            int listenPortMax,
            Func<string, DateTime, DateTime, Task> readData,
            ILogger logger)
        {
            _environmentVariables = environmentVariables;
            _readData = readData;
            _logger = logger;

            var listenPort = GetNextUnusedPort(listenPortMin, listenPortMax);

            command = CommandRegex().Replace(command, listenPort.ToString());
            var commandParts = command.Split(" ", count: 2);
            _command = commandParts[0];

            _arguments = commandParts.Length == 2
                ? commandParts[1]
                : "";

            _tcpListener = new TcpListener(listenAddress, listenPort);
            _tcpListener.Start();
        }

        #endregion

        #region Methods

        public async Task<IJsonRpcServer> ConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.Register(() => _tcpListener.Stop());
				
                // start process
                _logger.LogDebug("Start process.");
                
                var psi = new ProcessStartInfo(_command)
                {
                    Arguments = _arguments,
                };

                foreach (var variable in _environmentVariables)
                {
                    psi.EnvironmentVariables[variable.Key] = variable.Value;
                }

                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                _process = new Process() { StartInfo = psi };
                _process.Start();

                _process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        _logger.LogDebug("{Message}", e.Data);
                };

                _process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        _logger.LogWarning("{Message}", e.Data);
                };

                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                // wait for clients to connect
                _logger.LogDebug("Wait for clients to connect.");

                var filters = new string[] { "comm", "data" };

                Stream? commStream = default;
                Stream? dataStream = default;

                for (int i = 0; i < 2; i++)
                {
                    var (identifier, client) = await GetTcpClientAsync(filters, cancellationToken);

                    if (commStream is null && identifier == "comm")
                        commStream = client.GetStream();

                    else if (dataStream is null && identifier == "data")
                        dataStream = client.GetStream();
                }

                if (commStream is null || dataStream is null)
                    throw new Exception("The RPC server did not connect properly via communication and a data stream. This may indicate that other TCP clients have tried to connect.");

                _commStream = commStream;
                _dataStream = dataStream;

                var formatter = new JsonMessageFormatter()
                {
                    JsonSerializer = { 
                        ContractResolver = new DefaultContractResolver
                        {
                            NamingStrategy = new CamelCaseNamingStrategy()
                        }
                    }
                };

                formatter.JsonSerializer.Converters.Add(new JsonElementConverter());
                formatter.JsonSerializer.Converters.Add(new StringEnumConverter());

                var messageHandler = new LengthHeaderMessageHandler(commStream, commStream, formatter);
                var jsonRpc = new JsonRpc(messageHandler);

                jsonRpc.AddLocalRpcMethod("log", new Action<LogLevel, string>((logLevel, message) =>
                {
                    _logger.Log(logLevel, "{Message}", message);
                }));

                jsonRpc.AddLocalRpcMethod("readData", _readData);
                jsonRpc.StartListening();

                _rpcServer = jsonRpc.Attach<IJsonRpcServer>(new JsonRpcProxyOptions()
                {
                    MethodNameTransform = pascalCaseAsyncName =>
                    {
                        return char.ToLower(pascalCaseAsyncName[0]) + pascalCaseAsyncName[1..].Replace("Async", string.Empty);
                    }
                });

                return _rpcServer;
            }
            catch
            {
                try
                {
                    _process?.Kill();
                }
                catch {
                    //
                }
                
                throw;
            }
            finally
            {
                _tcpListener.Stop();
            }
        }

        public Task ReadRawAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            return InternalReadRawAsync(buffer, _dataStream, cancellationToken);
        }

        private static async Task InternalReadRawAsync(Memory<byte> buffer, Stream source, CancellationToken cancellationToken)
        {
            while (buffer.Length > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var readCount = await source.ReadAsync(buffer, cancellationToken);

                if (readCount == 0)
                    throw new Exception("The TCP connection closed early.");

                buffer = buffer[readCount..];
            }
        }

        public Task WriteRawAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            return InternalWriteRawAsync(buffer, _dataStream, cancellationToken);
        }

        private static async Task InternalWriteRawAsync(ReadOnlyMemory<byte> buffer, Stream target, CancellationToken cancellationToken)
        {
            var length = BitConverter.GetBytes(buffer.Length).Reverse().ToArray();

            await target.WriteAsync(length, cancellationToken);
            await target.WriteAsync(buffer, cancellationToken);
            await target.FlushAsync(cancellationToken);
        }

        private static int GetNextUnusedPort(int min, int max)
        {
            lock (_lock)
            {
                min = Math.Max(_nextMin, min);

                if (max <= min)
                    throw new ArgumentException("Max port cannot be less than or equal to min.");

                var ipProperties = IPGlobalProperties.GetIPGlobalProperties();

                var usedPorts =
                    ipProperties.GetActiveTcpConnections()
                        .Where(connection => connection.State != TcpState.Closed)
                        .Select(connection => connection.LocalEndPoint)
                        .Concat(ipProperties.GetActiveTcpListeners())
                        .Select(endpoint => endpoint.Port)
                        .ToArray();

                var firstUnused =
                    Enumerable.Range(min, max - min)
                        .Where(port => !usedPorts.Contains(port))
                        .Select(port => new int?(port))
                        .FirstOrDefault();

                if (!firstUnused.HasValue)
                    throw new Exception($"All TCP ports in the range of {min}..{max} are currently in use.");

                _nextMin = (firstUnused.Value + 1) % max;
                return firstUnused.Value;
            }
        }

        private async Task<(string Identifier, TcpClient Client)> GetTcpClientAsync(string[] filters, CancellationToken cancellationToken)
        {
            var buffer = new byte[4];
            var client = await _tcpListener.AcceptTcpClientAsync(cancellationToken);

            await InternalReadRawAsync(buffer, client.GetStream(), cancellationToken);

            foreach (var filter in filters)
            {
                if (buffer.SequenceEqual(Encoding.UTF8.GetBytes(filter)))
                    return (filter, client);
            }

            throw new Exception("Invalid stream identifier received.");
        }

        #endregion

        #region IDisposable

        private bool _disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    try
                    {
                        var disposable = _rpcServer as IDisposable;
                        disposable?.Dispose();

                        _commStream?.Dispose();
                        _dataStream?.Dispose();

                        try
                        {
                            _process?.Kill();
                        }
                        catch
                        {
                            //
                        }
                    }
                    catch (Exception)
                    {
                        //
                    }

                    //_process?.WaitForExitAsync();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        [GeneratedRegex("{remote-port}")]
        private static partial Regex CommandRegex();

        #endregion
    }
}
