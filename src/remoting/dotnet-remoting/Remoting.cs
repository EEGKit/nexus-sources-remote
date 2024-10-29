using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;
using System.Buffers;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Nexus.Remoting;

internal class Logger(NetworkStream commStream) : ILogger
{
    private readonly NetworkStream _commStream = commStream;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        throw new NotImplementedException("Scopes are not supported on this logger.");
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel, 
        EventId eventId, 
        TState state, 
        Exception? exception, 
        Func<TState, Exception?, string> formatter
    )
    {
        var notification = new JsonObject()
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "log",
            ["params"] = new JsonArray(logLevel.ToString(), formatter(state, exception))
        };

        _ = Utilities.SendToServerAsync(notification, _commStream);
    }
}

/// <summary>
/// A remote communicator.
/// </summary>
public class RemoteCommunicator
{
    private readonly TcpClient _comm;

    private readonly TcpClient _data;

    private readonly NetworkStream _commStream;

    private readonly NetworkStream _dataStream;

    private readonly Func<string, IDataSource> _getDataSource;

    private ILogger _logger = default!;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteCommunicator" />.
    /// </summary>
    /// <param name="comm">The TCP client for communications.</param>
    /// <param name="data">The TCP client for data.</param>
    /// <param name="getDataSource">A func to get a new data source instance by its type name.</param>
    public RemoteCommunicator(
        TcpClient comm, 
        TcpClient data, 
        Func<string, IDataSource> getDataSource
    )
    {
        _comm = comm;
        _commStream = comm.GetStream();

        _data = data;
        _dataStream = data.GetStream();

        _getDataSource = getDataSource;
    }

    /// <summary>
    /// Starts the remoting operation.
    /// </summary>
    /// <returns></returns>
    public async Task RunAsync()
    {
        static JsonElement Read(Span<byte> jsonRequest)
        {
            var reader = new Utf8JsonReader(jsonRequest);
            return JsonSerializer.Deserialize<JsonElement>(ref reader, Utilities.Options);
        }

        // loop
        while (true)
        {
            // https://www.jsonrpc.org/specification

            // get request message
            var size = ReadSize(_commStream);

            using var memoryOwner = MemoryPool<byte>.Shared.Rent(size);
            var messageMemory = memoryOwner.Memory[..size];

            _commStream.ReadExactly(messageMemory.Span, _logger);
            var request = Read(messageMemory.Span);

            // process message
            Memory<byte> data = default;
            Memory<byte> status = default;
            JsonObject? response;

            if (request.TryGetProperty("jsonrpc", out var element) &&
                element.ValueKind == JsonValueKind.String &&
                element.GetString() == "2.0")
            {
                if (request.TryGetProperty("id", out var _))
                {
                    try
                    {
                        (var result, data, status) = await ProcessInvocationAsync(request);

                        response = new JsonObject()
                        {
                            ["result"] = result
                        };
                    }
                    catch (Exception ex)
                    {
                        response = new JsonObject()
                        {
                            ["error"] = new JsonObject()
                            {
                                ["code"] = -1,
                                ["message"] = ex.ToString()
                            }
                        };
                    }
                }
                else
                {
                    throw new Exception($"JSON-RPC 2.0 notifications are not supported.");
                }
            }
            else
            {
                throw new Exception($"JSON-RPC 2.0 message expected, but got something else.");
            }

            response.Add("jsonrpc", "2.0");

            string? id;

            if (request.TryGetProperty("id", out var element2))
                id = element2.ToString();

            else
                throw new Exception("Unable to read the request message id.");

            response.Add("id", id);

            // send response
            await Utilities.SendToServerAsync(response, _commStream);

            // send data
            if (!data.Equals(default) && !status.Equals(default))
            {
                await _dataStream.WriteAsync(data);
                await _dataStream.WriteAsync(status);
                await _dataStream.FlushAsync();
            }
        }
    }

    private async Task<(JsonObject?, Memory<byte>, Memory<byte>)> ProcessInvocationAsync(JsonElement request)
    {
#warning Use strongly typed deserialization instead?

        JsonObject? result = default;
        Memory<byte> data = default;
        Memory<byte> status = default;
        IDataSource? dataSource = default;

        var methodName = request.GetProperty("method").GetString();
        var @params = request.GetProperty("params");

        if (methodName == "getApiVersion")
        {
            result = new JsonObject()
            {
                ["apiVersion"] = 1
            };
        }

        else if (methodName == "setContext")
        {
            var rawContext = @params[0];
            var resourceLocator = default(Uri?);

            if (rawContext.TryGetProperty("type", out var type))
                dataSource = _getDataSource(type.ToString());

            else
                throw new Exception("The type property is required");

            if (rawContext.TryGetProperty("resourceLocator", out var value))
                resourceLocator = new Uri(value.GetString()!);

            // system configuration
            IReadOnlyDictionary<string, JsonElement>? systemConfiguration = default;

            if (rawContext.TryGetProperty("systemConfiguration", out var systemConfigurationElement))
                systemConfiguration = JsonSerializer.Deserialize<IReadOnlyDictionary<string, JsonElement>?>(systemConfigurationElement);

            // source configuration 
            IReadOnlyDictionary<string, JsonElement>? sourceConfiguration = default;

            if (rawContext.TryGetProperty("sourceConfiguration", out var sourceConfigurationElement))
                sourceConfiguration = JsonSerializer.Deserialize<IReadOnlyDictionary<string, JsonElement>?>(sourceConfigurationElement);

            // request configuration 
            IReadOnlyDictionary<string, JsonElement>? requestConfiguration = default;

            if (rawContext.TryGetProperty("requestConfiguration", out var requestConfigurationElement))
                requestConfiguration = JsonSerializer.Deserialize<IReadOnlyDictionary<string, JsonElement>?>(requestConfigurationElement);

            _logger = new Logger(_commStream);

            var context = new DataSourceContext(
                resourceLocator,
                systemConfiguration,
                sourceConfiguration,
                requestConfiguration
            );

            await dataSource.SetContextAsync(context, _logger, CancellationToken.None);
        }

        else if (methodName == "getCatalogRegistrations")
        {
            if (dataSource is null)
                throw new Exception("The data source context must be set before invoking other methods.");

            var path = @params[0].GetString()!;
            var registrations = await dataSource.GetCatalogRegistrationsAsync(path, CancellationToken.None);

            result = new JsonObject()
            {
                ["registrations"] = JsonSerializer.SerializeToNode(registrations, Utilities.Options)
            };
        }

        else if (methodName == "getCatalog")
        {
            if (dataSource is null)
                throw new Exception("The data source context must be set before invoking other methods.");

            var catalogId = @params[0].GetString()!;
            var catalog = await dataSource.GetCatalogAsync(catalogId, CancellationToken.None);

            result = new JsonObject()
            {
                ["catalog"] = JsonSerializer.SerializeToNode(catalog, Utilities.Options)
            };
        }

        else if (methodName == "getTimeRange")
        {
            if (dataSource is null)
                throw new Exception("The data source context must be set before invoking other methods.");

            var catalogId = @params[0].GetString()!;
            var (begin, end) = await dataSource.GetTimeRangeAsync(catalogId, CancellationToken.None);

            result = new JsonObject()
            {
                ["begin"] = begin,
                ["end"] = end
            };
        }

        else if (methodName == "getAvailability")
        {
            if (dataSource is null)
                throw new Exception("The data source context must be set before invoking other methods.");

            var catalogId = @params[0].GetString()!;

            var beginString = @params[1].GetString()!;
            var begin = DateTime.ParseExact(beginString, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

            var endString = @params[2].GetString()!;
            var end = DateTime.ParseExact(endString, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

            var availability = await dataSource.GetAvailabilityAsync(catalogId, begin, end, CancellationToken.None);

            result = new JsonObject()
            {
                ["availability"] = availability
            };
        }

        else if (methodName == "readSingle")
        {
            if (dataSource is null)
                throw new Exception("The data source context must be set before invoking other methods.");

            var beginString = @params[0].GetString()!;
            var begin = DateTime.ParseExact(beginString, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture).ToUniversalTime();

            var endString = @params[1].GetString()!;
            var end = DateTime.ParseExact(endString, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture).ToUniversalTime();

            var catalogItem = JsonSerializer.Deserialize<CatalogItem>(@params[2], Utilities.Options)!;
            (data, status) = ExtensibilityUtilities.CreateBuffers(catalogItem.Representation, begin, end);
            var readRequest = new ReadRequest(catalogItem, data, status);

            await dataSource.ReadAsync(
                begin,
                end,
                [readRequest],
                HandleReadDataAsync,
                new Progress<double>(),
                CancellationToken.None
            );
        }

        // Add cancellation support?
        // https://github.com/microsoft/vs-streamjsonrpc/blob/main/doc/sendrequest.md#cancellation
        // https://github.com/Microsoft/language-server-protocol/blob/main/versions/protocol-2-x.md#cancelRequest
        else if (methodName == "$/cancelRequest")
        {
            //
        }

        // Add progress support?
        // https://github.com/microsoft/vs-streamjsonrpc/blob/main/doc/progresssupport.md
        else if (methodName == "$/progress")
        {
            //
        }

        // Add OOB stream support?
        // https://github.com/microsoft/vs-streamjsonrpc/blob/main/doc/oob_streams.md

        else
            throw new Exception($"Unknown method '{methodName}'.");

        return (result, data, status);
    }

    private async Task HandleReadDataAsync(
        string resourcePath,
        DateTime begin,
        DateTime end,
        Memory<double> buffer,
        CancellationToken cancellationToken)
    {
        var readDataRequest = new JsonObject()
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "readData",
            ["params"] = new JsonArray(resourcePath, begin, end)
        };

        _logger.LogDebug("Read resource path {ResourcePath} from Nexus", resourcePath);

        await Utilities.SendToServerAsync(readDataRequest, _commStream);

        var size = ReadSize(_dataStream);

        if (size != buffer.Length * sizeof(double))
            throw new Exception("Data returned by Nexus have an unexpected length");

        _logger.LogTrace("Try to read {ByteCount} bytes from Nexus", size);

        _dataStream.ReadExactly(MemoryMarshal.AsBytes(buffer.Span), _logger);
    }

    private int ReadSize(NetworkStream currentStream)
    {
        Span<byte> sizeBuffer = stackalloc byte[4];
        currentStream.ReadExactly(sizeBuffer, _logger);
        MemoryExtensions.Reverse(sizeBuffer);

        var size = BitConverter.ToInt32(sizeBuffer);
        return size;
    }
}

internal static class Utilities
{
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    static Utilities()
    {
        Options = new JsonSerializerOptions()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        Options.Converters.Add(new JsonStringEnumConverter());
    }

    public static JsonSerializerOptions Options { get; }

    public static async Task SendToServerAsync(JsonNode response, NetworkStream currentStream)
    {
        var encodedResponse = JsonSerializer.SerializeToUtf8Bytes(response, Options);
        var messageLength = BitConverter.GetBytes(encodedResponse.Length);
        Array.Reverse(messageLength);

        await _semaphore.WaitAsync(TimeSpan.FromMinutes(1));

        try
        {
            await currentStream.WriteAsync(messageLength);
            await currentStream.WriteAsync(encodedResponse);
            await currentStream.FlushAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }
}

internal static class StreamExtensions
{
    public static void ReadExactly(this Stream stream, Span<byte> buffer, ILogger logger)
    {
        while (buffer.Length > 0)
        {
            var read = stream.Read(buffer);

            if (read == 0)
            {
                logger.LogDebug("No data from Nexus received (exiting)");
                Environment.Exit(0);
            }

            buffer = buffer[read..];
        }
    }
}

internal class CastMemoryManager<TFrom, TTo>(Memory<TFrom> from) : MemoryManager<TTo>
        where TFrom : struct
        where TTo : struct
{
    private readonly Memory<TFrom> _from = from;

    public override Span<TTo> GetSpan() => MemoryMarshal.Cast<TFrom, TTo>(_from.Span);

    protected override void Dispose(bool disposing)
    {
        //
    }

    public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException("CastMemoryManager does not support pinning.");

    public override void Unpin() => throw new NotSupportedException("CastMemoryManager does not support unpinning.");
}