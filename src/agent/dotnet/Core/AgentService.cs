using Apollo3zehn.PackageManagement.Services;
using Microsoft.Extensions.Options;
using Nexus.Extensibility;
using Nexus.Remoting;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Nexus.Agent.Core;

public class TcpClientPair
{
    public NetworkStream? Comm { get; set; }

    public NetworkStream? Data { get; set; }

    public RemoteCommunicator? RemoteCommunicator { get; set; }

    public CancellationTokenSource CancellationTokenSource { get; } = new();
}

internal class AgentService
{
    private static readonly TimeSpan CLIENT_TIMEOUT = TimeSpan.FromMinutes(1);

    private readonly Lock _lock = new();

    private readonly ConcurrentDictionary<Guid, TcpClientPair> _tcpClientPairs = new();

    private readonly IExtensionHive<IDataSource> _extensionHive;

    private readonly IPackageService _packageService;

    private readonly ILogger<AgentService> _agentLogger;

    private readonly SystemOptions _systemOptions;

    public AgentService(
        IExtensionHive<IDataSource> extensionHive,
        IPackageService packageService,
        ILogger<AgentService> agentLogger,
        IOptions<SystemOptions> systemOptions)
    {
        _extensionHive = extensionHive;
        _packageService = packageService;
        _agentLogger = agentLogger;
        _systemOptions = systemOptions.Value;
    }

    public async Task LoadPackagesAsync(CancellationToken cancellationToken)
    {
        _agentLogger.LogInformation("Load packages");

        var packageReferenceMap = await _packageService.GetAllAsync();
        var progress = new Progress<double>();

        await _extensionHive.LoadPackagesAsync(
            packageReferenceMap: packageReferenceMap,
            progress,
            cancellationToken
        );
    }

    public Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        _agentLogger.LogInformation(
            "Listening for JSON-RPC communication on {JsonRpcListenAddress}:{JsonRpcListenPort}",
            _systemOptions.JsonRpcListenAddress, _systemOptions.JsonRpcListenPort
        );

        var tcpListener = new TcpListener(
            IPAddress.Parse(_systemOptions.JsonRpcListenAddress),
            _systemOptions.JsonRpcListenPort
        );

        tcpListener.Start();

        // Detect and remove inactivate clients
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(10));

                foreach (var (key, pair) in _tcpClientPairs)
                {
                    if (pair.RemoteCommunicator!.LastCommunication >= CLIENT_TIMEOUT)
                    {
                        if (_tcpClientPairs.TryRemove(key, out var _))
                            pair.CancellationTokenSource.Cancel();
                    }
                }
            }
        });

        // Accept new clients and start communication
        return Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await tcpListener.AcceptTcpClientAsync(cancellationToken);

                _ = Task.Run(async () =>
                {
                    if (!client.Connected)
                        throw new Exception("client is not connected");

                    var streamReadCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    var networkStream = client.GetStream(); /* no 'using' because it would close the TCP client */

                    // get connection id
                    var buffer1 = new byte[36];
                    await networkStream.ReadExactlyAsync(buffer1, streamReadCts.Token);
                    var idString = Encoding.UTF8.GetString(buffer1);

                    // get connection type
                    var buffer2 = new byte[4];
                    await networkStream.ReadExactlyAsync(buffer2, streamReadCts.Token);
                    var typeString = Encoding.UTF8.GetString(buffer2);

                    if (Guid.TryParse(Encoding.UTF8.GetString(buffer1), out var id))
                    {
                        _agentLogger.LogDebug("Accept TCP client with connection ID {ConnectionId} and communication type {CommunicationType}", idString, typeString);

                        var tcpPairCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                        // Handle the timeout event
                        tcpPairCts.Token.Register(() =>
                        {
                            // If TCP client pair can be found ...
                            if (_tcpClientPairs.TryGetValue(id, out var pair))
                            {
                                // and if TCP client pair is not yet complete ...
                                if (pair.Comm is null || pair.Data is null)
                                {
                                    // then dispose and remove the clients and the pair
                                    pair.Comm?.Dispose();
                                    pair.Data?.Dispose();

                                    _tcpClientPairs.Remove(id, out _);
                                }
                            }
                        });

                        // We got a "comm" tcp connection
                        if (typeString == "comm")
                        {
                            _tcpClientPairs.AddOrUpdate(
                                id, 
                                addValueFactory: id => new TcpClientPair { Comm = networkStream },
                                updateValueFactory: (id, pair) => 
                                {
                                    pair.Comm?.Dispose();
                                    pair.Comm = networkStream;
                                    return pair;
                                }
                            );
                        }

                        // We got a "data" tcp connection
                        else if (typeString == "data")
                        {
                            _tcpClientPairs.AddOrUpdate(
                                id, 
                                addValueFactory: id => new TcpClientPair { Data = networkStream },
                                updateValueFactory: (id, pair) => 
                                {
                                    pair.Data?.Dispose();
                                    pair.Data = networkStream;
                                    return pair;
                                }
                            );
                        }

                        // Something went wrong, dispose the network stream and return
                        else
                        {
                            networkStream.Dispose();
                            return;
                        }

                        var pair = _tcpClientPairs[id];

                        lock (_lock)
                        {
                            if (pair.Comm is not null && pair.Data is not null && pair.RemoteCommunicator is null)
                            {
                                _agentLogger.LogDebug("Accept remoting client with connection ID {ConnectionId}", id);

                                try
                                {
                                    pair.RemoteCommunicator = new RemoteCommunicator(
                                        pair.Comm,
                                        pair.Data,
                                        getDataSource: type => _extensionHive.GetInstance(type)
                                    );

                                    _ = pair.RemoteCommunicator.RunAsync(pair.CancellationTokenSource.Token);
                                }
                                catch
                                {
                                    pair.Comm?.Dispose();
                                    pair.Data?.Dispose();

                                    throw;
                                }
                            }
                        }
                    }
                });
            }
        });
    }
}