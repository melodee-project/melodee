using System.Text.Json;
using System.Text.Json.Serialization;
using Melodee.Common.Models;
using Melodee.Common.Models.OpenSubsonic.Responses;

namespace Melodee.Common.Serialization.Convertors;

public class OpenSubsonicResponseModelConvertor : JsonConverter<ResponseModel>
{
    public override ResponseModel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);

        if (!doc.RootElement.TryGetProperty("subsonic-response", out var root))
        {
            // Accept flat payloads as a fallback
            root = doc.RootElement;
        }

        var status = root.TryGetProperty("status", out var statusEl) &&
                     statusEl.GetString()?.Equals("ok", StringComparison.OrdinalIgnoreCase) == true;

        // Core metadata
        var version = root.TryGetProperty("version", out var verEl) ? verEl.GetString() ?? string.Empty : string.Empty;
        var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? string.Empty : string.Empty;
        var serverVersion = root.TryGetProperty("serverVersion", out var svEl) ? svEl.GetString() ?? string.Empty : string.Empty;

        // Error block (optional)
        Models.OpenSubsonic.Error? error = null;
        if (root.TryGetProperty("error", out var errorEl) && errorEl.ValueKind == JsonValueKind.Object)
        {
            short code = 0;
            string message = string.Empty;
            if (errorEl.TryGetProperty("code", out var codeEl))
            {
                _ = short.TryParse(codeEl.ToString(), out code);
            }
            if (errorEl.TryGetProperty("message", out var msgEl))
            {
                message = msgEl.GetString() ?? string.Empty;
            }
            error = new Models.OpenSubsonic.Error(code, message);
        }

        // Find first data-like property that is not core metadata
        string? dataPropertyName = null;
        object? data = null;
        string? dataDetailPropertyName = null;

        foreach (var prop in root.EnumerateObject())
        {
            var name = prop.Name;
            if (string.Equals(name, "status", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "version", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "type", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "serverVersion", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "openSubsonic", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "error", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Consider the first remaining property as the data container
            dataPropertyName = name;
            var value = prop.Value;

            if (value.ValueKind == JsonValueKind.Object)
            {
                // If the object has a single property, assume it's the detail collection name
                using var obj = JsonDocument.Parse(value.GetRawText());
                var rootObj = obj.RootElement;
                if (rootObj.EnumerateObject() is var objEnum && objEnum.MoveNext())
                {
                    var firstChild = objEnum.Current;
                    if (!objEnum.MoveNext())
                    {
                        dataDetailPropertyName = firstChild.Name;
                        data = firstChild.Value.Clone();
                        break;
                    }
                }
            }

            data = value.Clone();
            break;
        }

        var apiResponse = new ApiResponse
        {
            IsSuccess = status,
            Version = version,
            Type = type,
            ServerVersion = serverVersion,
            Error = error,
            DataPropertyName = dataPropertyName ?? string.Empty,
            DataDetailPropertyName = dataDetailPropertyName,
            Data = data
        };

        return new ResponseModel
        {
            IsSuccess = status,
            ApiKeyId = null,
            UserInfo = UserInfo.BlankUserInfo,
            ResponseData = apiResponse,
            TotalCount = 0
        };
    }

    public override void Write(Utf8JsonWriter writer, ResponseModel value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("subsonic-response");

        writer.WriteStartObject();

        writer.WritePropertyName("status");
        writer.WriteStringValue(value.IsSuccess ? "ok" : "error");
        writer.WritePropertyName("version");
        writer.WriteStringValue(value.ResponseData.Version);
        writer.WritePropertyName("type");
        writer.WriteStringValue(value.ResponseData.Type);
        writer.WritePropertyName("serverVersion");
        writer.WriteStringValue(value.ResponseData.ServerVersion);
        writer.WritePropertyName("openSubsonic");
        writer.WriteBooleanValue(true);

        var hasData = value.ResponseData.Data != null;
        var hasDetailPropertyName = !string.IsNullOrEmpty(value.ResponseData.DataDetailPropertyName);

        // Only write DataPropertyName if we have data or need the wrapper object
        if (!string.IsNullOrEmpty(value.ResponseData.DataPropertyName) && (hasData || hasDetailPropertyName))
        {
            writer.WritePropertyName(value.ResponseData.DataPropertyName);
        }

        if (hasDetailPropertyName)
        {
            writer.WriteStartObject();
        }

        if (hasData)
        {
            if (hasDetailPropertyName)
            {
                writer.WritePropertyName(value.ResponseData.DataDetailPropertyName!);
            }

            writer.WriteRawValue(JsonSerializer.Serialize(value.ResponseData.Data, Serializer.JsonSerializerOptions));
        }

        if (hasDetailPropertyName)
        {
            writer.WriteEndObject();
        }

        if (value.ResponseData.Error != null)
        {
            writer.WritePropertyName("error");
            writer.WriteRawValue(JsonSerializer.Serialize(value.ResponseData.Error, Serializer.JsonSerializerOptions));
        }

        writer.WriteEndObject();

        writer.WriteEndObject();
    }
}
