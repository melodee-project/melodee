using System.Text.Json;
using Melodee.Common.Serialization;

namespace Melodee.Common.Services.ScriptEvaluation;

public static class ScriptValueConverter
{
    public static object? ToScriptValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(value, Serializer.JsonSerializerOptions);
        using var document = JsonDocument.Parse(json);
        return ConvertElement(document.RootElement);
    }

    private static object? ConvertElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ConvertObject(element),
            JsonValueKind.Array => ConvertArray(element),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => ConvertNumber(element),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => null
        };
    }

    private static Dictionary<string, object?> ConvertObject(JsonElement element)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            dictionary[property.Name] = ConvertElement(property.Value);
        }

        return dictionary;
    }

    private static List<object?> ConvertArray(JsonElement element)
    {
        var list = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ConvertElement(item));
        }

        return list;
    }

    private static object ConvertNumber(JsonElement element)
    {
        if (element.TryGetInt64(out var l))
        {
            return l;
        }

        return element.GetDouble();
    }
}

