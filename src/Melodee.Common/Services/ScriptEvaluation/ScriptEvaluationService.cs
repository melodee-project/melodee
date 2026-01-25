using Jint;
using Melodee.Common.Models.Scripting;
using Serilog;

namespace Melodee.Common.Services.ScriptEvaluation;

public interface IScriptEvaluationService
{
    Task<ScriptEvaluationResult> EvaluateScriptAsync(
        string scriptBody,
        object context,
        object scriptConfig,
        ScriptConfig config,
        CancellationToken cancellationToken = default);
}

public sealed class ScriptEvaluationService : IScriptEvaluationService
{
    private readonly IScriptCacheService _cacheService;
    private readonly ILogger _logger;

    public ScriptEvaluationService(
        ILogger logger,
        IScriptCacheService cacheService)
    {
        _logger = logger;
        _cacheService = cacheService;
    }

    public async Task<ScriptEvaluationResult> EvaluateScriptAsync(
        string scriptBody,
        object context,
        object scriptConfig,
        ScriptConfig config,
        CancellationToken cancellationToken = default)
    {
        if (!config.Enabled)
        {
            return new ScriptEvaluationResult
            {
                Result = true,
                IsDefault = true,
                ErrorMessage = null
            };
        }

        if (string.IsNullOrWhiteSpace(scriptBody))
        {
            return new ScriptEvaluationResult
            {
                Result = true,
                IsDefault = true,
                ErrorMessage = "Script body is empty, defaulting to allow"
            };
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var effectiveScriptBody = NormalizeToCheckFunction(scriptBody);
            var preparedScriptKey = ScriptHashing.Sha256Hex(effectiveScriptBody);
            var preparedScript = await _cacheService
                .GetOrCreatePreparedScriptAsync(preparedScriptKey, effectiveScriptBody, cancellationToken)
                .ConfigureAwait(false);

            var engine = new Engine(options =>
            {
                options.Strict = true;
                options.TimeoutInterval(TimeSpan.FromMilliseconds(config.TimeoutMs));
                options.MaxStatements(config.MaxStatements);
            });

            engine.Execute(preparedScript);

            var contextValue = ScriptValueConverter.ToScriptValue(context);
            var scriptConfigValue = ScriptValueConverter.ToScriptValue(scriptConfig);

            // Reset constraints so time/statement limits apply to the check() invocation,
            // not to host-side setup work (engine initialization, script loading, value conversion).
            engine.Constraints.Reset();

            var result = engine.Invoke("check", contextValue, scriptConfigValue);

            stopwatch.Stop();

            if (!result.IsBoolean())
            {
                return new ScriptEvaluationResult
                {
                    Result = true,
                    IsDefault = true,
                    Duration = stopwatch.Elapsed,
                    ErrorMessage = "Script returned a non-boolean value, defaulting to allow"
                };
            }

            return new ScriptEvaluationResult
            {
                Result = result.AsBoolean(),
                IsDefault = false,
                SelectedOverrideId = null,
                Duration = stopwatch.Elapsed,
                // TODO this needs to be implemented
                // The result can be a simple bool or an object with a message, when the result has "message" property use it here
                Message = null,
                ErrorMessage = null
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Error(ex, "Script evaluation failed");

            return new ScriptEvaluationResult
            {
                Result = true,
                IsDefault = true,
                Duration = stopwatch.Elapsed,
                ErrorMessage = ex.Message
            };
        }
    }

    private static string NormalizeToCheckFunction(string scriptBody)
    {
        var trimmed = scriptBody.Trim();
        if (trimmed.Contains("function check", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return $"function check(ctx, scriptConfig) {{ return ({trimmed}); }}";
    }
}
