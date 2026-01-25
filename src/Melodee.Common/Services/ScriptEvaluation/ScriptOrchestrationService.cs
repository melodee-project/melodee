using Melodee.Common.Models.Scripting;
using Serilog;

namespace Melodee.Common.Services.ScriptEvaluation;

public interface IScriptOrchestrationService
{
    Task<ScriptEvaluationResult> EvaluateScriptForEventAsync(
        string eventName,
        object context,
        int libraryId,
        string relativePath,
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
        int libraryId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var config = await _configurationService.GetScriptConfigAsync(eventName, cancellationToken);

        if (config == null || !config.Enabled)
        {
            return new ScriptEvaluationResult
            {
                Result = true,
                IsDefault = true,
                ErrorMessage = null
            };
        }

        var selectedOverride = ScriptOverrideSelector.SelectOverride(config, libraryId, relativePath);
        var isOverride = selectedOverride != null;

        var onDeny = selectedOverride?.OnDeny ?? config.Default.OnDeny;

        var scriptBody = isOverride
            ? selectedOverride!.Body
            : !string.IsNullOrWhiteSpace(config.Default.Body)
                ? config.Default.Body
                : config.DefaultBody ?? string.Empty;

        if (string.IsNullOrWhiteSpace(scriptBody))
        {
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
            onDeny,
            isOverride,
            libraryId = selectedOverride?.LibraryId,
            pathPrefix = selectedOverride?.PathPrefix
        };

        var evaluationResult = await _evaluationService
            .EvaluateScriptAsync(scriptBody, context, scriptConfig, config, cancellationToken)
            .ConfigureAwait(false);

        if (evaluationResult.ErrorMessage != null)
        {
            _logger.Warning(
                "Script evaluation failure defaulted to allow. Event {EventName} key {ScriptKey} hash {ScriptHash} library {LibraryId} path {RelativePath} override {OverrideId}. Error: {ErrorMessage}",
                eventName,
                config.SettingKey,
                scriptHash,
                libraryId,
                relativePath,
                isOverride ? $"{selectedOverride!.LibraryId}|{selectedOverride.PathPrefix}" : "default",
                evaluationResult.ErrorMessage);
        }

        return evaluationResult with
        {
            IsDefault = !isOverride,
            SelectedOverrideId = isOverride ? $"{selectedOverride!.LibraryId}|{selectedOverride.PathPrefix}" : null,
            ScriptKey = config.SettingKey,
            ScriptHash = scriptHash,
            Message = evaluationResult.ErrorMessage,
            OnDeny = onDeny
        };
    }
}
