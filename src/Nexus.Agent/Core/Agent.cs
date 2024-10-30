using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Remoting;
using Nexus.Services;

namespace Nexus.Agent;

public class Agent
{
    private Lock _lock = new();

    private readonly ConcurrentDictionary<Guid, TcpClientPair> _tcpClientPairs = new();

    public async Task RunAsync()
    {
        var extensionHive = await LoadPackagesAsync();
        await AcceptClientsAsync(extensionHive);
    }

    private async Task<IExtensionHive> LoadPackagesAsync()
    {
        var pathsOptions = Options.Create(new PathsOptions());
        var loggerFactory = new LoggerFactory();

        var databaseService = new DatabaseService(pathsOptions);
        var packageService = new PackageService(databaseService);

        var extensionHive = new ExtensionHive(
            pathsOptions, 
            NullLogger<ExtensionHive>.Instance, loggerFactory
        );

        var packageReferenceMap = await packageService.GetAllAsync();
        var progress = new Progress<double>();

        await extensionHive.LoadPackagesAsync(
            packageReferenceMap: packageReferenceMap,
            progress,
            CancellationToken.None
        );

        return extensionHive;
    }

    private Task AcceptClientsAsync(IExtensionHive extensionHive)
    {
        var tcpListener = new TcpListener(IPAddress.Any, 56145);
        tcpListener.Start();

        return Task.Run(async () =>
        {
            while (true)
            {
                var client = await tcpListener.AcceptTcpClientAsync();

                _ = Task.Run(async () =>
                {
                    var streamReadCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    using var stream = client.GetStream();

                    // get connection id
                    var buffer1 = new byte[36];
                    await stream.ReadExactlyAsync(buffer1, streamReadCts.Token);
                    var idString = Encoding.UTF8.GetString(buffer1);

                    // get connection type
                    var buffer2 = new byte[4];
                    await stream.ReadExactlyAsync(buffer2, streamReadCts.Token);
                    var typeString = Encoding.UTF8.GetString(buffer2);

                    if (Guid.TryParse(Encoding.UTF8.GetString(buffer1), out var id))
                    {
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
                                addValueFactory: id => new TcpClientPair { Comm = client },
                                updateValueFactory: (id, pair) => 
                                {
                                    pair.Comm?.Dispose();
                                    pair.Comm = client;
                                    return pair;
                                }
                            );
                        }

                        // We got a "data" tcp connection
                        else if (typeString == "data")
                        {
                            _tcpClientPairs.AddOrUpdate(
                                id, 
                                addValueFactory: id => new TcpClientPair { Data = client },
                                updateValueFactory: (id, pair) => 
                                {
                                    pair.Data?.Dispose();
                                    pair.Data = client;
                                    return pair;
                                }
                            );
                        }

                        // Something went wrong, dispose the client
                        else
                        {
                            client.Dispose();
                            return;
                        }

                        var pair = _tcpClientPairs[id];

                        lock (_lock)
                        {
                            if (pair.Comm is not null && pair.Data is not null && pair.RemoteCommunicator is null)
                            {
                                try
                                {
                                    pair.RemoteCommunicator = new RemoteCommunicator(
                                        pair.Comm,
                                        pair.Data,
                                        getDataSource: type => extensionHive.GetInstance<IDataSource>(type)
                                    );

                                    _ = pair.RemoteCommunicator.RunAsync();
                                }
                                catch
                                {
                                    pair.Comm?.Dispose();
                                    pair.Data?.Dispose();
                                }
                            }
                        }
                    }
                });
            }
        });
    }
}

public class TcpClientPair
{
    public TcpClient? Comm { get; set; }

    public TcpClient? Data { get; set; }

    public RemoteCommunicator? RemoteCommunicator { get; set; }
}