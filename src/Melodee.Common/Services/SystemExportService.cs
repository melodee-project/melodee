using System.Text.Json;
using System.Text.Json.Serialization;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Melodee.Common.Services;

public sealed class SystemExportService
{
    private const string SchemaVersion = "1.0";
    private static readonly string[] SecretPatterns = { "secret", "token", "password" };
    private readonly ILogger _logger;
    private readonly ICacheManager _cacheManager;
    private readonly IMelodeeConfigurationFactory _configurationFactory;
    private readonly IDbContextFactory<MelodeeDbContext> _contextFactory;

    public SystemExportService(
        ILogger logger,
        ICacheManager cacheManager,
        IMelodeeConfigurationFactory configurationFactory,
        IDbContextFactory<MelodeeDbContext> contextFactory)
    {
        _logger = logger;
        _cacheManager = cacheManager;
        _configurationFactory = configurationFactory;
        _contextFactory = contextFactory;
    }

    public async Task<ExportResult> ExportAsync(bool redactSecrets = true, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var settings = await db.Settings
                .OrderBy(s => s.Key)
                .ToListAsync(cancellationToken);

            var libraries = await db.Libraries
                .OrderBy(l => l.Type)
                .ThenBy(l => l.Name)
                .ToListAsync(cancellationToken);

            var exportedSettings = settings.Select(s => new ExportedSetting
            {
                Key = s.Key,
                Value = ShouldRedact(s.Key) && redactSecrets ? "[REDACTED]" : s.Value,
                Comment = s.Comment,
                Category = s.Category
            }).ToList();

            var exportedLibraries = libraries.Select(l => new ExportedLibrary
            {
                Name = l.Name,
                Type = l.TypeValue.ToString(),
                Path = l.Path,
                ApiKey = l.ApiKey.ToString(),
                Description = l.Description
            }).ToList();

            var exportData = new SystemExportData
            {
                SchemaVersion = SchemaVersion,
                ExportedAt = DateTimeOffset.UtcNow.ToString("O"),
                Settings = exportedSettings,
                Libraries = exportedLibraries
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            return new ExportResult
            {
                Success = true,
                Json = JsonSerializer.Serialize(exportData, jsonOptions),
                SettingsCount = exportedSettings.Count,
                LibrariesCount = exportedLibraries.Count
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export system data");
            return new ExportResult
            {
                Success = false,
                ErrorMessage = $"Export failed: {ex.Message}"
            };
        }
    }

    private static bool ShouldRedact(string key)
    {
        var lowerKey = key.ToLowerInvariant();
        return SecretPatterns.Any(pattern => lowerKey.Contains(pattern));
    }
}

public sealed class SystemExportData
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonPropertyName("exportedAt")]
    public string ExportedAt { get; init; } = string.Empty;

    [JsonPropertyName("settings")]
    public List<ExportedSetting> Settings { get; init; } = new();

    [JsonPropertyName("libraries")]
    public List<ExportedLibrary> Libraries { get; init; } = new();
}

public sealed class ExportedSetting
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("category")]
    public int? Category { get; init; }
}

public sealed class ExportedLibrary
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public sealed class ExportResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Json { get; init; }
    public int SettingsCount { get; init; }
    public int LibrariesCount { get; init; }
}
