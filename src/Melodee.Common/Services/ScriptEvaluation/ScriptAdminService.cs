using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Filtering;
using Melodee.Common.Models;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Serialization;
using NodaTime;
using Serilog;

namespace Melodee.Common.Services.ScriptEvaluation;

public record ScriptSettingSummary
{
    public int SettingId { get; init; }
    public string EventName { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public int OverridesCount { get; init; }
    public string DefaultOnDeny { get; init; } = "skip";
    public string LastUpdatedUtc { get; init; } = string.Empty;
    public bool IsValid { get; init; }
    public string? ParseError { get; init; }
}

public record ScriptSettingDetail
{
    public Setting Setting { get; init; } = null!;
    public ScriptConfig Config { get; init; } = new();
}

public interface IScriptAdminService
{
    Task<IReadOnlyList<ScriptSettingSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<ScriptSettingDetail?> GetAsync(string eventName, CancellationToken cancellationToken = default);
    Task<OperationResult<bool>> UpsertAsync(string eventName, ScriptConfig config, CancellationToken cancellationToken = default);
    Task<OperationResult<bool>> DeleteAsync(string eventName, CancellationToken cancellationToken = default);
}

public sealed class ScriptAdminService : IScriptAdminService
{
    private const string ScriptKeyTemplate = "script.{0}";

    private readonly SettingService _settingService;
    private readonly ISerializer _serializer;
    private readonly ILogger _logger;

    public ScriptAdminService(
        SettingService settingService,
        ISerializer serializer,
        ILogger logger)
    {
        _settingService = settingService;
        _serializer = serializer;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScriptSettingSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = await _settingService.ListAsync(new PagedRequest
        {
            FilterBy =
            [
                new FilterOperatorInfo(nameof(Setting.Key), FilterOperator.StartsWith, "script.")
            ],
            PageSize = short.MaxValue
        }, cancellationToken).ConfigureAwait(false);

        var settings = result.Data as Setting[] ?? result.Data.ToArray();

        return settings
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(CreateSummary)
            .ToArray();
    }

    public async Task<ScriptSettingDetail?> GetAsync(string eventName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return null;
        }

        var key = string.Format(ScriptKeyTemplate, eventName);
        var settingResult = await _settingService.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (!settingResult.IsSuccess || settingResult.Data == null)
        {
            return null;
        }

        var setting = settingResult.Data;
        ScriptConfig config;

        try
        {
            config = _serializer.Deserialize<ScriptConfig>(setting.Value) ?? new ScriptConfig();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to deserialize script config for {Key}", key);
            config = new ScriptConfig();
        }

        return new ScriptSettingDetail
        {
            Setting = setting,
            Config = config
        };
    }

    public async Task<OperationResult<bool>> UpsertAsync(string eventName, ScriptConfig config, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return new OperationResult<bool>
            {
                Data = false,
                Type = OperationResponseType.ValidationFailure
            };
        }

        var key = string.Format(ScriptKeyTemplate, eventName);
        var serializedConfig = _serializer.Serialize(config) ?? "{}";

        var existing = await _settingService.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (existing.IsSuccess && existing.Data != null)
        {
            existing.Data.Value = serializedConfig;
            existing.Data.Category = (int)SettingCategory.Scripting;
            existing.Data.Comment = existing.Data.Comment.Nullify() ?? $"Event script config for {eventName}";

            var updateResult = await _settingService.UpdateAsync(existing.Data, cancellationToken).ConfigureAwait(false);
            return new OperationResult<bool>(updateResult.Messages ?? [])
            {
                Data = updateResult.Data,
                Type = updateResult.Type
            };
        }

        var addResult = await _settingService.AddAsync(new Setting
        {
            Key = key,
            Value = serializedConfig,
            Category = (int)SettingCategory.Scripting,
            Comment = $"Event script config for {eventName}",
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            Description = null,
            Notes = null,
            Tags = null,
            SortOrder = 0,
            IsLocked = false
        }, cancellationToken).ConfigureAwait(false);

        return new OperationResult<bool>(addResult.Messages ?? [])
        {
            Data = addResult.IsSuccess,
            Type = addResult.Type
        };
    }

    public async Task<OperationResult<bool>> DeleteAsync(string eventName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return new OperationResult<bool>
            {
                Data = false,
                Type = OperationResponseType.ValidationFailure
            };
        }

        var key = string.Format(ScriptKeyTemplate, eventName);
        return await _settingService.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
    }

    private ScriptSettingSummary CreateSummary(Setting setting)
    {
        try
        {
            var config = _serializer.Deserialize<ScriptConfig>(setting.Value) ?? new ScriptConfig();
            var updated = setting.LastUpdatedAt ?? setting.CreatedAt;

            return new ScriptSettingSummary
            {
                SettingId = setting.Id,
                EventName = setting.Key.StartsWith("script.", StringComparison.OrdinalIgnoreCase)
                    ? setting.Key["script.".Length..]
                    : setting.Key,
                Enabled = config.Enabled,
                OverridesCount = config.Overrides.Count,
                DefaultOnDeny = config.Default.OnDeny,
                LastUpdatedUtc = updated.ToDateTimeUtc().ToString("O"),
                IsValid = true,
                ParseError = null
            };
        }
        catch (Exception ex)
        {
            var updated = setting.LastUpdatedAt ?? setting.CreatedAt;

            return new ScriptSettingSummary
            {
                SettingId = setting.Id,
                EventName = setting.Key.StartsWith("script.", StringComparison.OrdinalIgnoreCase)
                    ? setting.Key["script.".Length..]
                    : setting.Key,
                Enabled = false,
                OverridesCount = 0,
                DefaultOnDeny = "skip",
                LastUpdatedUtc = updated.ToDateTimeUtc().ToString("O"),
                IsValid = false,
                ParseError = ex.Message
            };
        }
    }
}
