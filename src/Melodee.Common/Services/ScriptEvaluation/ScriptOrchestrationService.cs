using Melodee.Common.Models.Scripting;
using Serilog;

namespace Melodee.Common.Services.ScriptEvaluation;

public interface IScriptOrchestrationService
{
    Task<ScriptEvaluationResult> EvaluateScriptForEventAsync(
        string eventName,
        object context,
        CancellationToken cancellationToken = default);
}

public sealed class ScriptOrchestrationService : IScriptOrchestrationService
{
    private readonly IScriptConfigurationService _configurationService;
    private readonly IScriptEvaluationService _evaluationService;
    private readonly ILogger _logger;

    public ScriptOrchestrationService(
        IScriptConfigurationService configurationService,
        IScriptEvaluationService evaluationService,
        ILogger logger)
    {
        _configurationService = configurationService;
        _evaluationService = evaluationService;
        _logger = logger;
    }

    public async Task<ScriptEvaluationResult> EvaluateScriptForEventAsync(
        string eventName,
        object context,
        CancellationToken cancellationToken = default)
    {
        _logger.Debug("Evaluating script for event {EventName}", eventName);
        
        var config = await _configurationService.GetScriptConfigAsync(eventName, cancellationToken);

        if (config == null || !config.Enabled)
        {
            _logger.Debug(
                "Script for event {EventName} is {Status}",
                eventName, config == null ? "not configured" : "disabled");
            return new ScriptEvaluationResult
            {
                Result = true,
                IsDefault = true,
                ErrorMessage = null
            };
        }

        var onDeny = config.Default.OnDeny;

        var scriptBody = !string.IsNullOrWhiteSpace(config.Default.Body)
            ? config.Default.Body
            : config.DefaultBody ?? string.Empty;

        if (string.IsNullOrWhiteSpace(scriptBody))
        {
            _logger.Debug(
                "Script for event {EventName} has no body, defaulting to allow",
                eventName);
            return new ScriptEvaluationResult
            {
                Result = true,
                IsDefault = true,
                SelectedOverrideId = null,
                ScriptKey = config.SettingKey,
                ScriptHash = null,
                OnDeny = onDeny,
                ErrorMessage = "No script body available, defaulting to allow"
            };
        }

        var scriptHash = ScriptHashing.Sha256Hex(scriptBody);

        var scriptConfig = new
        {
            eventName,
            settingKey = config.SettingKey,
            timeoutMs = config.TimeoutMs,
            maxStatements = config.MaxStatements,
            onDeny
        };

        _logger.Debug(
            "Executing script for event {EventName} key {ScriptKey} hash {ScriptHash}",
            eventName, config.SettingKey, scriptHash);

        var evaluationResult = await _evaluationService
            .EvaluateScriptAsync(scriptBody, context, scriptConfig, config, cancellationToken)
            .ConfigureAwait(false);

        _logger.Debug(
            "Script for event {EventName} returned Result={Result} Message={Message}",
            eventName, evaluationResult.Result, evaluationResult.Message ?? "(none)");

        if (evaluationResult.ErrorMessage != null)
        {
            _logger.Warning(
                "Script evaluation failure defaulted to allow. Event {EventName} key {ScriptKey} hash {ScriptHash}. Error: {ErrorMessage}",
                eventName,
                config.SettingKey,
                scriptHash,
                evaluationResult.ErrorMessage);
        }

        return evaluationResult with
        {
            IsDefault = evaluationResult.ErrorMessage != null,
            SelectedOverrideId = null,
            ScriptKey = config.SettingKey,
            ScriptHash = scriptHash,
            Message = evaluationResult.ErrorMessage,
            OnDeny = onDeny
        };
    }
}
