using Melodee.Common.Data.Models;
using Melodee.Common.Filtering;
using Melodee.Common.Models;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Serialization;
using NodaTime;
using Serilog;

namespace Melodee.Common.Services.ScriptEvaluation;

public interface IScriptConfigurationService
{
    Task<ScriptConfig?> GetScriptConfigAsync(string eventName, CancellationToken cancellationToken = default);
}

public sealed class ScriptConfigurationService : IScriptConfigurationService
{
    private const string ScriptSettingKeyTemplate = "script.{0}";
    private readonly SettingService _settingService;
    private readonly ISerializer _serializer;
    private readonly ILogger _logger;

    public ScriptConfigurationService(
        SettingService settingService,
        ISerializer serializer,
        ILogger logger)
    {
        _settingService = settingService;
        _serializer = serializer;
        _logger = logger;
    }

    public async Task<ScriptConfig?> GetScriptConfigAsync(string eventName, CancellationToken cancellationToken = default)
    {
        var settingKey = string.Format(ScriptSettingKeyTemplate, eventName);

        try
        {
            var result = await _settingService.ListAsync(new PagedRequest
            {
                FilterBy =
                [
                    new FilterOperatorInfo(nameof(Setting.Key), FilterOperator.Equals, settingKey)
                ],
                PageSize = 1
            }, cancellationToken);

            var settings = result.Data as Setting[] ?? result.Data.ToArray();
            if (settings.Length == 0)
            {
                return null;
            }

            var setting = settings[0];
            var config = _serializer.Deserialize<ScriptConfig>(setting.Value);
            if (config == null)
            {
                return null;
            }

            var etagInstant = setting.LastUpdatedAt ?? setting.CreatedAt;

            return config with
            {
                SettingKey = settingKey,
                SettingEtag = etagInstant.ToUnixTimeMilliseconds().ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load script configuration for event {EventName}", eventName);
            return null;
        }
    }
}
