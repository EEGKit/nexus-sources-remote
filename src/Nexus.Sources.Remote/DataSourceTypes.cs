using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Nexus.DataModel;
using Nexus.Extensibility;
using System.Text.Json;

namespace Nexus.Sources
{
    internal interface IJsonRpcServer
    {
        public Task<ApiVersionResponse> 
            GetApiVersionAsync(CancellationToken cancellationToken);

        public Task 
            SetContextAsync(DataSourceContext context, CancellationToken cancellationToken);

        public Task<CatalogRegistrationsResponse>
            GetCatalogRegistrationsAsync(string path, CancellationToken cancellationToken);

        public Task<CatalogResponse>
            GetCatalogAsync(string catalogId, CancellationToken cancellationToken);

        public Task<TimeRangeResponse>
            GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken);

        public Task<AvailabilityResponse>
            GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken);

        public Task
            ReadSingleAsync(DateTime begin, DateTime end, CatalogItem catalogItem, CancellationToken cancellationToken);
    }

    internal record ApiVersionResponse(int ApiVersion);
    internal record CatalogRegistrationsResponse(CatalogRegistration[] Registrations);
    internal record CatalogResponse(ResourceCatalog Catalog);
    internal record TimeRangeResponse(DateTime Begin, DateTime End);
    internal record AvailabilityResponse(double Availability);
    internal record LogMessage(LogLevel LogLevel, string Message);

    internal class RemoteException : Exception
    {
        public RemoteException(string message, Exception? innerException = default)
            : base(message, innerException)
        {
            //
        }
    }

    internal class JsonElementConverter : Newtonsoft.Json.JsonConverter
    {
        internal static JsonSerializerOptions _serializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public override bool CanConvert(Type objectType)
        {
            var canConvert = objectType == typeof(JsonElement);
            return canConvert;
        }

        public override object? ReadJson(Newtonsoft.Json.JsonReader reader, Type objectType, object? existingValue, Newtonsoft.Json.JsonSerializer serializer)
        {
            if (reader.TokenType == Newtonsoft.Json.JsonToken.Null)
                return default;

            if (reader.TokenType == Newtonsoft.Json.JsonToken.String)
                return JsonSerializer.SerializeToElement(JToken.Load(reader).ToString());
          
            var serialized_tmp = JToken.Load(reader).ToString();
            var deserialized = JsonSerializer.Deserialize<JsonElement>(serialized_tmp);
            return deserialized;
        }

        public override void WriteJson(Newtonsoft.Json.JsonWriter writer, object? value, Newtonsoft.Json.JsonSerializer serializer)
        {        
            var jsonString = JsonSerializer.Serialize(value, _serializerOptions);
            writer.WriteRawValue(jsonString);
        }
    }
}
