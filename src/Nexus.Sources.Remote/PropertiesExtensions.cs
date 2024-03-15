using System.Text.Json;

namespace Nexus.Sources;

internal static class PropertiesExtensions
{
    public static int? GetIntValue(this IReadOnlyDictionary<string, JsonElement> properties, string propertyPath)
    {
        var pathSegments = propertyPath.Split('/').AsSpan();

        if (properties.TryGetValue(pathSegments[0], out var element))
        {
            pathSegments = pathSegments[1..];

            if (pathSegments.Length == 0)
            {
                if (element.ValueKind == JsonValueKind.Number)
                    return element.GetInt32();
            }

            else
            {
                var newPropertyPath = string.Join('/', pathSegments.ToArray());
                return element.GetIntValue(newPropertyPath);
            }
        }

        return default;
    }

    public static int? GetIntValue(this JsonElement properties, string propertyPath)
    {
        var pathSegments = propertyPath.Split('/').AsSpan();
        var root = properties.GetJsonObjectFromPath(pathSegments[0..^1]);

        var propertyName = pathSegments.Length == 0
            ? propertyPath
            : pathSegments[^1];

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var propertyValue) &&
            (propertyValue.ValueKind == JsonValueKind.Number))
            return propertyValue.GetInt32();

        return default;
    }

    private static JsonElement GetJsonObjectFromPath(this JsonElement root, Span<string> pathSegements)
    {
        if (pathSegements.Length == 0)
            return root;

        var current = root;

        foreach (var pathSegement in pathSegements)
        {
            if (current.ValueKind == JsonValueKind.Object &&
                current.TryGetProperty(pathSegement, out current))
            {
                // do nothing   
            }
            else
            {
                return default;
            }
        }

        return current;
    }
}