using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;

namespace Nexus.Sources;

internal interface IJsonRpcServer
{
    public Task<ApiVersionResponse>
        GetApiVersionAsync(CancellationToken cancellationToken);

    public Task
        SetContextAsync(string type, DataSourceContext context, CancellationToken cancellationToken);

    public Task<CatalogRegistrationsResponse>
        GetCatalogRegistrationsAsync(string path, CancellationToken cancellationToken);

    public Task<CatalogResponse>
        EnrichCatalogAsync(ResourceCatalog catalog, CancellationToken cancellationToken);

    public Task<TimeRangeResponse>
        GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken);

    public Task<AvailabilityResponse>
        GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken);

    public Task
        ReadSingleAsync(DateTime begin, DateTime end, string originalResourceName, CatalogItem catalogItem, CancellationToken cancellationToken);
}

internal record ApiVersionResponse(int ApiVersion);
internal record CatalogRegistrationsResponse(CatalogRegistration[] Registrations);
internal record CatalogResponse(ResourceCatalog Catalog);
internal record TimeRangeResponse(DateTime Begin, DateTime End);
internal record AvailabilityResponse(double Availability);
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