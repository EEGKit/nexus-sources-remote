using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;

namespace Nexus.Sources;

internal interface IJsonRpcServer : IDataSource<JsonElement>
{
    public Task<int> InitializeAsync(
        string type,
        CancellationToken cancellationToken
    );

    public Task<JsonElement> UpgradeSourceConfigurationAsync(
        JsonElement configuration, 
        CancellationToken cancellationToken
    );

    public Task SetContextAsync(
        DataSourceContext<JsonElement> context, 
        CancellationToken cancellationToken
    );

    public Task ReadSingleAsync(
        DateTime begin, 
        DateTime end, 
        string originalResourceName, 
        CatalogItem catalogItem, 
        CancellationToken cancellationToken
    );
}

internal record LogMessage(LogLevel LogLevel, string Message);

internal class RemoteException(string message, Exception? innerException = default) : Exception(message, innerException)
{
}

internal class RoundtripDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (!DateTime.TryParseExact
            (
                reader.GetString(), 
                "o",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal,
                out var dateTime
            )
        )
        {
            throw new JsonException();
        }

        return dateTime;
    }

    public override void Write(
        Utf8JsonWriter writer, 
        DateTime value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStringValue(value.ToString("o", CultureInfo.InvariantCulture));
    }
}