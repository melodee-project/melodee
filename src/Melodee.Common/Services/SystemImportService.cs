using System.Text.Json;
using System.Text.Json.Serialization;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Models;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;

namespace Melodee.Common.Services;

public sealed class SystemImportService
{
    private const string SchemaVersion = "1.0";
    private readonly ILogger _logger;
    private readonly ICacheManager _cacheManager;
    private readonly IMelodeeConfigurationFactory _configurationFactory;
    private readonly IDbContextFactory<MelodeeDbContext> _contextFactory;
    private readonly SettingService _settingService;
    private readonly LibraryService _libraryService;

    public SystemImportService(
        ILogger logger,
        ICacheManager cacheManager,
        IMelodeeConfigurationFactory configurationFactory,
        IDbContextFactory<MelodeeDbContext> contextFactory)
    {
        _logger = logger;
        _cacheManager = cacheManager;
        _configurationFactory = configurationFactory;
        _contextFactory = contextFactory;
        _settingService = new SettingService(logger, cacheManager, configurationFactory, contextFactory);
        _libraryService = new LibraryService(logger, cacheManager, contextFactory, configurationFactory, null!, null!);
    }

    public async Task<ImportResult> ImportAsync(string jsonContent, CancellationToken cancellationToken = default)
    {
        ImportData? importData;
        try
        {
            importData = JsonSerializer.Deserialize<ImportData>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.Error(ex, "Failed to parse import JSON");
            return new ImportResult
            {
                Success = false,
                ErrorMessage = "Invalid JSON format"
            };
        }

        if (importData == null)
        {
            return new ImportResult
            {
                Success = false,
                ErrorMessage = "No import data found"
            };
        }

        if (importData.SchemaVersion != SchemaVersion)
        {
            return new ImportResult
            {
                Success = false,
                ErrorMessage = $"Schema version mismatch. Expected {SchemaVersion}, got {importData.SchemaVersion}"
            };
        }

        var result = new ImportResult
        {
            Success = true
        };

        await using var transaction = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await transaction.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var existingSettings = await _settingService.GetAllKeysAsync(cancellationToken).ConfigureAwait(false);
            var environmentVariableKeys = MelodeeConfigurationFactory.EnvironmentVariablesSettings()
                .Select(x => x.Key.Replace("_", "."))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var setting in importData.Settings)
            {
                if (string.IsNullOrWhiteSpace(setting.Key))
                {
                    result.SettingsSkipped++;
                    continue;
                }

                if (environmentVariableKeys.Contains(setting.Key))
                {
                    result.SettingsSkipped++;
                    result.SkippedReasons.Add($"Setting '{setting.Key}' is set via environment variable");
                    continue;
                }

                var existingSetting = await _settingService.GetAsync(setting.Key, cancellationToken).ConfigureAwait(false);
                if (existingSetting.Data != null)
                {
                    if (existingSetting.Data.IsLocked)
                    {
                        result.SettingsSkipped++;
                        result.SkippedReasons.Add($"Setting '{setting.Key}' is locked");
                        continue;
                    }

                    existingSetting.Data.Value = setting.Value;
                    existingSetting.Data.Comment = setting.Comment ?? existingSetting.Data.Comment;
                    await _settingService.UpdateAsync(existingSetting.Data, cancellationToken).ConfigureAwait(false);
                    result.SettingsImported++;
                }
                else
                {
                    var newSetting = new Setting
                    {
                        Key = setting.Key,
                        Value = setting.Value,
                        Comment = setting.Comment,
                        Category = setting.Category ?? 0,
                        CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow),
                        ApiKey = Guid.NewGuid()
                    };
                    await _settingService.AddAsync(newSetting, cancellationToken).ConfigureAwait(false);
                    result.SettingsImported++;
                }
            }

            var existingLibraries = await _libraryService.ListAsync(new PagedRequest { PageSize = short.MaxValue }, cancellationToken).ConfigureAwait(false);
            var libraryTypeMap = new Dictionary<string, LibraryType>(StringComparer.OrdinalIgnoreCase)
            {
                { "Inbound", LibraryType.Inbound },
                { "Staging", LibraryType.Staging },
                { "Storage", LibraryType.Storage },
                { "UserImages", LibraryType.UserImages },
                { "Playlist", LibraryType.Playlist },
                { "Chart", LibraryType.Chart },
                { "Templates", LibraryType.Templates },
                { "Podcast", LibraryType.Podcast }
            };

            foreach (var lib in importData.Libraries)
            {
                if (string.IsNullOrWhiteSpace(lib.Name) || string.IsNullOrWhiteSpace(lib.Type))
                {
                    result.LibrariesSkipped++;
                    continue;
                }

                if (!libraryTypeMap.TryGetValue(lib.Type, out var libraryType))
                {
                    result.LibrariesSkipped++;
                    result.SkippedReasons.Add($"Unknown library type: {lib.Type}");
                    continue;
                }

                var existingLibrary = existingLibraries.Data.FirstOrDefault(l =>
                    l.Name.Equals(lib.Name, StringComparison.OrdinalIgnoreCase) ||
                    (l.TypeValue == libraryType && l.Path.Equals(lib.Path, StringComparison.OrdinalIgnoreCase)));

                if (existingLibrary != null)
                {
                    if (existingLibrary.IsLocked)
                    {
                        result.LibrariesSkipped++;
                        result.SkippedReasons.Add($"Library '{existingLibrary.Name}' is locked");
                        continue;
                    }

                    existingLibrary.Path = lib.Path;
                    existingLibrary.ApiKey = string.IsNullOrEmpty(lib.ApiKey) ? existingLibrary.ApiKey : Guid.Parse(lib.ApiKey);
                    existingLibrary.Description = lib.Description ?? existingLibrary.Description;
                    await _libraryService.UpdateAsync(existingLibrary, cancellationToken).ConfigureAwait(false);
                    result.LibrariesImported++;
                }
                else
                {
                    var newLibrary = new Library
                    {
                        Name = lib.Name,
                        Path = lib.Path,
                        Type = (int)libraryType,
                        ApiKey = string.IsNullOrEmpty(lib.ApiKey) ? Guid.NewGuid() : Guid.Parse(lib.ApiKey),
                        Description = lib.Description,
                        SortOrder = 0,
                        CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                    };
                    await _libraryService.CreateAsync(newLibrary, cancellationToken).ConfigureAwait(false);
                    result.LibrariesImported++;
                }
            }

            await transaction.Database.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);

            _configurationFactory.Reset();
            _cacheManager.Clear();

            return result;
        }
        catch (Exception ex)
        {
            await transaction.Database.RollbackTransactionAsync(cancellationToken).ConfigureAwait(false);
            _logger.Error(ex, "Failed to import system data");
            return new ImportResult
            {
                Success = false,
                ErrorMessage = $"Import failed: {ex.Message}"
            };
        }
    }
}

public sealed class ImportData
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonPropertyName("exportedAt")]
    public string ExportedAt { get; init; } = string.Empty;

    [JsonPropertyName("settings")]
    public List<ImportedSetting> Settings { get; init; } = new();

    [JsonPropertyName("libraries")]
    public List<ImportedLibrary> Libraries { get; init; } = new();
}

public sealed class ImportedSetting
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

public sealed class ImportedLibrary
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

public sealed class ImportResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int SettingsImported { get; set; }
    public int SettingsSkipped { get; set; }
    public int LibrariesImported { get; set; }
    public int LibrariesSkipped { get; set; }
    public List<string> SkippedReasons { get; set; } = new();
}
