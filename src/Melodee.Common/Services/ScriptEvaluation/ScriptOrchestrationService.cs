using Jint;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Services.Caching;
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
    private readonly IScriptCacheService _cacheService;
    private readonly IScriptEvaluationService _evaluationService;
    private readonly ILogger _logger;

    public ScriptOrchestrationService(
        IScriptConfigurationService configurationService,
        IScriptCacheService cacheService,
        IScriptEvaluationService evaluationService,
        ILogger logger)
    {
        _configurationService = configurationService;
        _cacheService = cacheService;
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

        var scriptBody = config.DefaultBody ?? string.Empty;
        var selectedOverride = ScriptOverrideSelector.SelectOverride(config, libraryId, relativePath);
        var isOverride = selectedOverride != null;

        if (isOverride)
        {
            scriptBody = selectedOverride!.Body;
        }

        if (string.IsNullOrWhiteSpace(scriptBody))
        {
            return new ScriptEvaluationResult
            {
                Result = true,
                IsDefault = true,
                SelectedOverrideId = null,
                ErrorMessage = "No script body available, defaulting to allow"
            };
        }

        var scriptHash = scriptBody.GetHashCode().ToString();
        Engine engine;

        try
        {
            engine = await _cacheService.GetOrCreateEngineAsync(scriptHash, scriptBody, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get or create cached script engine for event {EventName}", eventName);
            engine = new Engine(options =>
            {
                options.Strict = true;
                options.MaxStatements(config.MaxStatements);
            });
            engine.Execute(scriptBody);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = engine.Invoke("check", context);
            stopwatch.Stop();

            var boolResult = result.IsBoolean() && result.AsBoolean();

            return new ScriptEvaluationResult
            {
                Result = boolResult,
                IsDefault = !isOverride,
                SelectedOverrideId = isOverride ? $"lib:{libraryId}|path:{relativePath}" : null,
                Duration = stopwatch.Elapsed,
                ErrorMessage = null
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Error(ex, "Script evaluation failed for event {EventName}", eventName);

            return new ScriptEvaluationResult
            {
                Result = true,
                IsDefault = true,
                Duration = stopwatch.Elapsed,
                ErrorMessage = ex.Message
            };
        }
    }
}
