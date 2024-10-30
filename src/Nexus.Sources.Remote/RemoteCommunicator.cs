﻿using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace Nexus.Sources;

internal class RemoteCommunicator
{
    private readonly IPEndPoint _endpoint;

    private readonly TcpClient _comm = new();

    private readonly TcpClient _data = new();

    private NetworkStream? _commStream;

    private NetworkStream? _dataStream;

    private IJsonRpcServer _rpcServer = default!;

    private readonly ILogger _logger;

    private readonly Func<string, DateTime, DateTime, Task> _readData;

    public RemoteCommunicator(
        IPEndPoint endpoint,
        Func<string, DateTime, DateTime, Task> readData,
        ILogger logger
    )
    {
        _endpoint = endpoint;
        _readData = readData;
        _logger = logger;
    }

    public async Task<IJsonRpcServer> ConnectAsync(CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString();

        // comm connection
        await _comm.ConnectAsync(_endpoint, cancellationToken);
        _commStream = _comm.GetStream();

        await _commStream.WriteAsync(Encoding.UTF8.GetBytes(id), cancellationToken);
        await _commStream.WriteAsync(Encoding.UTF8.GetBytes("comm"), cancellationToken);
        await _commStream.FlushAsync(cancellationToken);

        // data connection
        await _data.ConnectAsync(_endpoint, cancellationToken);
        _dataStream = _data.GetStream();
        
        await _dataStream.WriteAsync(Encoding.UTF8.GetBytes(id), cancellationToken);
        await _dataStream.WriteAsync(Encoding.UTF8.GetBytes("data"), cancellationToken);
        await _dataStream.FlushAsync(cancellationToken);

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

        var messageHandler = new LengthHeaderMessageHandler(_commStream, _commStream, formatter);
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

    public ValueTask ReadRawAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (_dataStream is null)
            throw new Exception("You need to connect before read any data");

        return _dataStream.ReadExactlyAsync(buffer, cancellationToken);
    }

    public Task WriteRawAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (_dataStream is null)
            throw new Exception("You need to connect before write any data");

        return InternalWriteRawAsync(buffer, _dataStream, cancellationToken);
    }

    private static async Task InternalWriteRawAsync(
        ReadOnlyMemory<byte> buffer, 
        Stream target, 
        CancellationToken cancellationToken
    )
    {
        var length = BitConverter.GetBytes(buffer.Length).Reverse().ToArray();

        await target.WriteAsync(length, cancellationToken);
        await target.WriteAsync(buffer, cancellationToken);
        await target.FlushAsync(cancellationToken);
    }

#region IDisposable

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                var disposable = _rpcServer as IDisposable;
                disposable?.Dispose();

                _commStream?.Dispose();
                _dataStream?.Dispose();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    
#endregion
}