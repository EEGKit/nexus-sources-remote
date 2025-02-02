using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Sources;

internal static class Utilities
{
    static Utilities()
    {
        JsonSerializerOptions = new JsonSerializerOptions()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        JsonSerializerOptions.Converters.Add(new RoundtripDateTimeConverter());
    }

    public static JsonSerializerOptions JsonSerializerOptions { get; }
}